// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Background loop that drives the hand-off queue: on startup reclaims stuck (lease-expired) Running
/// rows, then repeatedly claims the next Pending operation, runs the executor while heartbeating the
/// lease, and writes the terminal status back. The updater never updates itself — only api/ui.
/// </summary>
/// <param name="store">Hand-off queue access</param>
/// <param name="executor">The update/rollback state machine</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;

namespace Klacks.Updater;

public class Worker : BackgroundService
{
    private readonly IUpdateOperationStore _store;
    private readonly UpdateExecutor _executor;
    private readonly IBackupRetentionService _retention;
    private readonly ILogger<Worker> _logger;

    public Worker(IUpdateOperationStore store, UpdateExecutor executor, IBackupRetentionService retention, ILogger<Worker> logger)
    {
        _store = store;
        _executor = executor;
        _retention = retention;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Klacks updater started.");

        try
        {
            var reclaimed = await _store.ReclaimStuckAsync(UpdaterConstants.StuckLeaseTimeout, stoppingToken);
            if (reclaimed > 0)
            {
                _logger.LogWarning("Reclaimed {Count} stuck operation(s) on startup.", reclaimed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reclaim stuck operations on startup.");
        }

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

    private async Task RunOperationAsync(UpdateOperation operation, CancellationToken stoppingToken)
    {
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeatTask = HeartbeatLoopAsync(operation.Id, heartbeatCts.Token);

        try
        {
            _logger.LogInformation("Executing {Type} operation {Id} (target {Target}).", operation.OperationType, operation.Id, operation.TargetVersion);
            var result = await _executor.ExecuteAsync(operation, stoppingToken);
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
