// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Background loop that drives the hand-off queue: on startup first recovers an interrupted engine upgrade, then
/// reclaims stuck (lease-expired) Running rows (retried until the database answers once), then repeatedly claims the next Pending operation, routes it by type to its executor while heartbeating the
/// lease, and writes the terminal status back. An unknown operation type fails without any change, so a newer row
/// can never be mistaken for an application update. A PostgreSQL engine upgrade queued longer ago than
/// EngineUpgradeConstants.PendingEngineUpgradeMaxAge is not executed but failed with a request-expired message, so a
/// forgotten request never takes the database down unannounced. The updater never updates itself — only api/ui.
/// </summary>
/// <param name="store">Hand-off queue access</param>
/// <param name="executor">The update/rollback state machine</param>
/// <param name="whisperExecutor">Installs or removes the self-hosted speech-to-text plugin</param>
/// <param name="ttsExecutor">Installs local text-to-speech into the klacks-stt service</param>
/// <param name="engineUpgradeExecutor">Runs PostgreSQL engine upgrades</param>
/// <param name="engineUpgradeRecovery">Finishes or reverts an engine upgrade interrupted by a crash, before anything else runs</param>
/// <param name="retention">Prunes old backups after every operation</param>
/// <param name="installedVersionReconciler">Aligns the operating system's installation entry with the running version on startup</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Application;
using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;

namespace Klacks.Updater;

public class Worker : BackgroundService
{
    private readonly IUpdateOperationStore _store;
    private readonly UpdateExecutor _executor;
    private readonly WhisperPluginExecutor _whisperExecutor;
    private readonly TtsInstallExecutor _ttsExecutor;
    private readonly EngineUpgradeExecutor _engineUpgradeExecutor;
    private readonly EngineUpgradeRecovery _engineUpgradeRecovery;
    private readonly IBackupRetentionService _retention;
    private readonly IInstalledVersionReconciler _installedVersionReconciler;
    private readonly ILogger<Worker> _logger;

    public Worker(
        IUpdateOperationStore store,
        UpdateExecutor executor,
        WhisperPluginExecutor whisperExecutor,
        TtsInstallExecutor ttsExecutor,
        EngineUpgradeExecutor engineUpgradeExecutor,
        EngineUpgradeRecovery engineUpgradeRecovery,
        IBackupRetentionService retention,
        IInstalledVersionReconciler installedVersionReconciler,
        ILogger<Worker> logger)
    {
        _store = store;
        _executor = executor;
        _whisperExecutor = whisperExecutor;
        _ttsExecutor = ttsExecutor;
        _engineUpgradeExecutor = engineUpgradeExecutor;
        _engineUpgradeRecovery = engineUpgradeRecovery;
        _retention = retention;
        _installedVersionReconciler = installedVersionReconciler;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Klacks updater started.");
        ReconcileInstalledVersion();

        await RecoverEngineUpgradeAsync(stoppingToken);
        await ReclaimStuckUntilDoneAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            UpdateOperation? operation = null;
            try
            {
                operation = await _store.ClaimNextPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to claim next pending operation.");
            }

            if (operation is null)
            {
                await Task.Delay(UpdaterConstants.PollInterval, stoppingToken);
                continue;
            }

            await RunOperationAsync(operation, stoppingToken);
        }
    }

    private async Task RecoverEngineUpgradeAsync(CancellationToken stoppingToken)
    {
        try
        {
            var recovered = await _engineUpgradeRecovery.RecoverAsync(stoppingToken);
            if (recovered is not null)
            {
                _logger.LogWarning("Recovered an interrupted engine upgrade: {Status} - {Message}", recovered.Status, recovered.Message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Recovering an interrupted engine upgrade failed.");
        }
    }

    private async Task ReclaimStuckUntilDoneAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                var reclaimed = await _store.ReclaimStuckAsync(UpdaterConstants.StuckLeaseTimeout, stoppingToken);
                if (reclaimed > 0)
                {
                    _logger.LogWarning("Reclaimed {Count} stuck operation(s) on startup.", reclaimed);
                }

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to reclaim stuck operations on startup; retrying.");
            }

            await Task.Delay(UpdaterConstants.StartupRetryInterval, stoppingToken);
        }
    }

    private void ReconcileInstalledVersion()
    {
        try
        {
            _installedVersionReconciler.Reconcile();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reconcile the installed version on startup.");
        }
    }

    private async Task RunOperationAsync(UpdateOperation operation, CancellationToken stoppingToken)
    {
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeatTask = HeartbeatLoopAsync(operation.Id, heartbeatCts.Token);

        try
        {
            _logger.LogInformation("Executing {Type} operation {Id} (target {Target}).", operation.OperationType, operation.Id, operation.TargetVersion);
            var result = await DispatchAsync(operation, stoppingToken);
            await _store.CompleteAsync(operation.Id, result.Status, result.Message, result.BackupRef, stoppingToken);
            _logger.LogInformation("Operation {Id} finished with status {Status}.", operation.Id, result.Status);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Operation {Id} crashed; marking RollbackFailed for manual recovery.", operation.Id);
            await SafeCompleteAsync(operation.Id, UpdateExecutionStatus.RollbackFailed, $"Updater crashed: {ex.Message}");
        }
        finally
        {
            await heartbeatCts.CancelAsync();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }

            await PruneBackupsAsync(stoppingToken);
        }
    }

    private async Task PruneBackupsAsync(CancellationToken stoppingToken)
    {
        try
        {
            var keepCount = await _store.GetBackupRetentionCountAsync(UpdaterConstants.DefaultBackupRetentionCount, stoppingToken);
            await _retention.PruneAsync(keepCount, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Backup retention pruning failed.");
        }
    }

    private async Task HeartbeatLoopAsync(Guid id, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await _store.HeartbeatAsync(id, token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat for {Id} failed.", id);
            }

            await Task.Delay(UpdaterConstants.HeartbeatInterval, token);
        }
    }

    private Task<UpdateExecutionResult> DispatchAsync(UpdateOperation operation, CancellationToken stoppingToken) =>
        operation.OperationType switch
        {
            UpdateOperationType.Update or UpdateOperationType.Rollback => _executor.ExecuteAsync(operation, stoppingToken),
            UpdateOperationType.WhisperInstall or UpdateOperationType.WhisperUninstall => _whisperExecutor.ExecuteAsync(operation, stoppingToken),
            UpdateOperationType.TtsInstall => _ttsExecutor.ExecuteAsync(operation, stoppingToken),
            UpdateOperationType.PostgresUpgrade when EngineUpgradeRequestExpiry.IsExpired(operation, DateTime.UtcNow) =>
                Task.FromResult(new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, EngineUpgradeConstants.ExpiredRequestMessage)),
            UpdateOperationType.PostgresUpgrade => _engineUpgradeExecutor.ExecuteAsync(operation, stoppingToken),
            _ => Task.FromResult(RejectUnknownType(operation)),
        };

    private UpdateExecutionResult RejectUnknownType(UpdateOperation operation)
    {
        _logger.LogError("Operation {Id} has unknown type {Type}; rejected without any change.", operation.Id, (int)operation.OperationType);
        return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, $"Unknown operation type {(int)operation.OperationType}." + UpdaterConstants.NothingChangedSuffix);
    }

    private async Task SafeCompleteAsync(Guid id, UpdateExecutionStatus status, string message)
    {
        try
        {
            await _store.CompleteAsync(id, status, message, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write terminal status for {Id}.", id);
        }
    }
}
