// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Ends an engine upgrade, used by the executor and by the startup recovery alike. Revert brings the old engine back
/// (stop application, RevertToOldEngineAsync, clear pools, start application, health gate with the version that ran
/// before) and yields RolledBack, or RollbackFailed when any step fails or the application stays unhealthy. Finish
/// always runs in the same order: terminal journal phase (Committed is retried until it is durable, because a journal
/// left before Committed would make the next start revert a finished upgrade), then the update_history row (retried until
/// the database answers), then the journal is archived, so a crash between two steps only repeats idempotent work.
/// </summary>
/// <param name="target">Deployment-specific engine upgrade steps</param>
/// <param name="snapshotReader">Clears connection pools after the engine behind the connection string changed</param>
/// <param name="healthGate">Waits until the application is healthy again</param>
/// <param name="journal">Durable journal of the open upgrade</param>
/// <param name="store">update_history access for the terminal status</param>
/// <param name="logger">Logger for diagnostic output</param>
/// <param name="retryInterval">Pause between retries of the journal and row writes (tests shorten it)</param>
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using Microsoft.Extensions.Logging;

namespace Klacks.Updater.Application.EngineUpgrade;

public class EngineUpgradeFinisher
{
    private readonly IEngineUpgradeTarget _target;
    private readonly IEngineSnapshotReader _snapshotReader;
    private readonly IUpdateHealthGate _healthGate;
    private readonly IEngineUpgradeJournal _journal;
    private readonly IUpdateOperationStore _store;
    private readonly ILogger<EngineUpgradeFinisher> _logger;
    private readonly TimeSpan _retryInterval;

    public EngineUpgradeFinisher(
        IEngineUpgradeTarget target,
        IEngineSnapshotReader snapshotReader,
        IUpdateHealthGate healthGate,
        IEngineUpgradeJournal journal,
        IUpdateOperationStore store,
        ILogger<EngineUpgradeFinisher> logger,
        TimeSpan? retryInterval = null)
    {
        _retryInterval = retryInterval ?? EngineUpgradeConstants.DatabaseRetryInterval;
        _target = target;
        _snapshotReader = snapshotReader;
        _healthGate = healthGate;
        _journal = journal;
        _store = store;
        _logger = logger;
    }

    public static EngineUpgradeContext ContextFrom(EngineUpgradeJournalEntry entry, string artifactRef, string artifactSha256) =>
        new(entry.OperationId, entry.FromMajor, entry.ToMajor, artifactRef, artifactSha256, entry.Old, entry.New);

    public async Task<UpdateExecutionResult> RevertAsync(EngineUpgradeJournalEntry entry, EngineUpgradeContext context, string reason, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Engine upgrade {Id} is reverted to PostgreSQL {Major} (phase {Phase}): {Reason}", entry.OperationId, entry.FromMajor, entry.Phase, reason);
        try
        {
            await StopApplicationQuietlyAsync(entry, cancellationToken);
            await _target.RevertToOldEngineAsync(context, cancellationToken);
            _snapshotReader.ClearConnectionPools();
            await _target.StartApplicationAsync(cancellationToken);

            if (await _healthGate.WaitForHealthyAsync(entry.AppVersion, cancellationToken))
            {
                return await FinishAsync(entry, UpdateExecutionStatus.RolledBack, $"Engine upgrade reverted, PostgreSQL {entry.FromMajor} is active again: {reason}", cancellationToken);
            }

            return await FinishAsync(entry, UpdateExecutionStatus.RollbackFailed, $"Engine upgrade reverted, but the application did not become healthy: {reason}", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reverting engine upgrade {Id} failed.", entry.OperationId);
            return await FinishAsync(entry, UpdateExecutionStatus.RollbackFailed, $"Reverting the engine upgrade failed ({ex.Message}) after: {reason}", cancellationToken);
        }
    }

    public async Task<UpdateExecutionResult?> TryRollForwardAsync(EngineUpgradeJournalEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            _snapshotReader.ClearConnectionPools();
            await _target.StartApplicationAsync(cancellationToken);
            if (await _healthGate.WaitForHealthyAsync(entry.AppVersion, cancellationToken))
            {
                _logger.LogWarning("Engine upgrade {Id} was interrupted after the configuration was committed; the application is healthy on PostgreSQL {Major}, committing.", entry.OperationId, entry.ToMajor);
                return await FinishAsync(
                    entry,
                    UpdateExecutionStatus.Succeeded,
                    $"PostgreSQL upgraded from {entry.FromMajor} to {entry.ToMajor} (completed after an interruption); the old cluster is kept for a manual rollback.",
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Rolling engine upgrade {Id} forward failed; reverting.", entry.OperationId);
        }

        return null;
    }

    public async Task<UpdateExecutionResult> FinishAsync(EngineUpgradeJournalEntry entry, UpdateExecutionStatus status, string message, CancellationToken cancellationToken)
    {
        message = message.Length > EngineUpgradeConstants.MaxMessageLength ? message[..EngineUpgradeConstants.MaxMessageLength] : message;
        entry.Phase = TerminalPhase(status);
        entry.Outcome = status.ToString();
        entry.Message = message;
        entry.Timestamps[entry.Phase.ToString()] = DateTime.UtcNow;
        await SaveTerminalJournalAsync(entry, status, cancellationToken);

        await CompleteRowAsync(entry.OperationId, status, message, cancellationToken);
        try
        {
            await _journal.ArchiveAsync(entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Archiving the journal of engine upgrade {Id} failed; the next start finishes it again.", entry.OperationId);
        }

        return new UpdateExecutionResult(status, null, message);
    }

    public static UpdateExecutionStatus StatusOf(EngineUpgradePhase phase) => phase switch
    {
        EngineUpgradePhase.Committed => UpdateExecutionStatus.Succeeded,
        EngineUpgradePhase.RolledBack => UpdateExecutionStatus.RolledBack,
        _ => UpdateExecutionStatus.RollbackFailed,
    };

    private static EngineUpgradePhase TerminalPhase(UpdateExecutionStatus status) => status switch
    {
        UpdateExecutionStatus.Succeeded => EngineUpgradePhase.Committed,
        UpdateExecutionStatus.RolledBack => EngineUpgradePhase.RolledBack,
        _ => EngineUpgradePhase.RollbackFailed,
    };

    private async Task StopApplicationQuietlyAsync(EngineUpgradeJournalEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _target.StopApplicationAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Stopping the application before reverting engine upgrade {Id} failed; reverting anyway.", entry.OperationId);
        }
    }

    private async Task SaveTerminalJournalAsync(EngineUpgradeJournalEntry entry, UpdateExecutionStatus status, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await _journal.SaveAsync(entry, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (status != UpdateExecutionStatus.Succeeded)
                {
                    _logger.LogError(ex, "Writing the terminal journal of engine upgrade {Id} failed.", entry.OperationId);
                    return;
                }

                _logger.LogError(ex, "Writing Committed into the journal of engine upgrade {Id} failed; retrying, the row stays Running until it is durable.", entry.OperationId);
            }

            await Task.Delay(_retryInterval, cancellationToken);
        }
    }

    private async Task CompleteRowAsync(Guid operationId, UpdateExecutionStatus status, string message, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await _store.CompleteAsync(operationId, status, message, null, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Writing status {Status} of engine upgrade {Id} failed; retrying.", status, operationId);
            }

            await Task.Delay(_retryInterval, cancellationToken);
        }
    }
}
