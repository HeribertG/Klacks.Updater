// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Startup recovery of an engine upgrade that was interrupted (crash, kill, service restart). Runs before the worker
/// reclaims stuck rows or claims new work. An open journal before Committed is reverted to the old engine first,
/// because the database may not even be reachable before that. From ConfigurationCommitted on the restore is verified and
/// users may already write to the new engine, so recovery first starts the application and commits if it is healthy
/// on the new engine (no data written since is lost); only otherwise it reverts. A journal already at Committed only gets its row set
/// to Succeeded; a journal at RolledBack/RollbackFailed only gets its row status written again. The row is written with
/// retries until the database answers, then the journal is archived. An unreadable journal is left in place and blocks
/// further engine upgrades until an administrator looks at it.
/// </summary>
/// <param name="journal">Durable journal of the open upgrade</param>
/// <param name="finisher">Revert and terminal bookkeeping</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using Microsoft.Extensions.Logging;

namespace Klacks.Updater.Application.EngineUpgrade;

public class EngineUpgradeRecovery
{
    public const string InterruptedMessage = "The updater was interrupted during the engine upgrade";

    private readonly IEngineUpgradeJournal _journal;
    private readonly EngineUpgradeFinisher _finisher;
    private readonly ILogger<EngineUpgradeRecovery> _logger;

    public EngineUpgradeRecovery(IEngineUpgradeJournal journal, EngineUpgradeFinisher finisher, ILogger<EngineUpgradeRecovery> logger)
    {
        _journal = journal;
        _finisher = finisher;
        _logger = logger;
    }

    public async Task<UpdateExecutionResult?> RecoverAsync(CancellationToken cancellationToken)
    {
        EngineUpgradeJournalEntry? entry;
        try
        {
            entry = await _journal.LoadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The engine upgrade journal cannot be read; it is left in place for manual inspection.");
            return null;
        }

        if (entry is null)
        {
            return null;
        }

        _logger.LogWarning("Found engine upgrade journal of operation {Id} in phase {Phase}; recovering.", entry.OperationId, entry.Phase);

        if (entry.Phase is EngineUpgradePhase.Committed or EngineUpgradePhase.RolledBack or EngineUpgradePhase.RollbackFailed)
        {
            return await _finisher.FinishAsync(
                entry,
                EngineUpgradeFinisher.StatusOf(entry.Phase),
                entry.Message ?? entry.Phase.ToString(),
                cancellationToken);
        }

        if (entry.Phase >= EngineUpgradePhase.ConfigurationCommitted)
        {
            var committed = await _finisher.TryRollForwardAsync(entry, cancellationToken);
            if (committed is not null)
            {
                return committed;
            }
        }

        var context = EngineUpgradeFinisher.ContextFrom(entry, string.Empty, string.Empty);
        return await _finisher.RevertAsync(entry, context, $"{InterruptedMessage} (phase {entry.Phase}).", cancellationToken);
    }
}
