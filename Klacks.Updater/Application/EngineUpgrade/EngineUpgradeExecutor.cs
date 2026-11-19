// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Runs a PostgreSQL engine upgrade operation (type PostgresUpgrade) as an ordered, journaled state machine: signature
/// and prechecks without any change, then artifact, stop application, content snapshot, dump, stop old engine,
/// activate new engine, restore, verification plus ANALYZE, commit configuration, start application and health gate,
/// commit. The journal is written after every phase. Until Committed the old cluster stays authoritative; any failure
/// reverts to it (RolledBack, or RollbackFailed when the revert fails). Cancellation leaves the journal open for the
/// startup recovery. Never throws otherwise.
/// </summary>
/// <param name="target">Deployment-specific engine upgrade steps</param>
/// <param name="snapshotReader">Cluster facts, content snapshots and ANALYZE</param>
/// <param name="verifier">Signature check of the engine artifact</param>
/// <param name="versionReader">Reads the running application version for the health gate</param>
/// <param name="healthGate">Waits until the application is healthy</param>
/// <param name="journal">Durable journal of the open upgrade</param>
/// <param name="finisher">Revert and terminal bookkeeping shared with the recovery</param>
/// <param name="options">Updater configuration (mode, test-only fault injection)</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Application.EngineUpgrade;

public class EngineUpgradeExecutor
{
    public const string SignatureRejectedMessage = "Engine artifact signature verification failed.";
    public const string PrecheckFailedMessage = "Reading the engine or cluster state failed.";
    public const string JournalOpenMessage = "Another engine upgrade journal is still open.";
    public const string FaultInjectedMessage = "Fault injected after phase";

    private readonly IEngineUpgradeTarget _target;
    private readonly IEngineSnapshotReader _snapshotReader;
    private readonly IArtifactVerifier _verifier;
    private readonly IApplicationVersionReader _versionReader;
    private readonly IUpdateHealthGate _healthGate;
    private readonly IEngineUpgradeJournal _journal;
    private readonly EngineUpgradeFinisher _finisher;
    private readonly UpdaterOptions _options;
    private readonly ILogger<EngineUpgradeExecutor> _logger;

    public EngineUpgradeExecutor(
        IEngineUpgradeTarget target,
        IEngineSnapshotReader snapshotReader,
        IArtifactVerifier verifier,
        IApplicationVersionReader versionReader,
        IUpdateHealthGate healthGate,
        IEngineUpgradeJournal journal,
        EngineUpgradeFinisher finisher,
        IOptions<UpdaterOptions> options,
        ILogger<EngineUpgradeExecutor> logger)
    {
        _target = target;
        _snapshotReader = snapshotReader;
        _verifier = verifier;
        _versionReader = versionReader;
        _healthGate = healthGate;
        _journal = journal;
        _finisher = finisher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(UpdateOperation operation, CancellationToken cancellationToken = default)
    {
        if (!_verifier.Verify(operation))
        {
            return Reject(operation, SignatureRejectedMessage);
        }

        if (_journal.Exists())
        {
            return Reject(operation, JournalOpenMessage);
        }

        EngineInspection inspection;
        EngineClusterFacts facts;
        string? applicationVersion;
        try
        {
            inspection = await _target.InspectAsync(cancellationToken);
            facts = await _snapshotReader.ReadClusterFactsAsync(cancellationToken);
            applicationVersion = await _versionReader.ReadRunningVersionAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Engine upgrade {Id}: precheck failed.", operation.Id);
            return Reject(operation, PrecheckFailedMessage);
        }

        var reason = EngineUpgradePrecheck.Evaluate(operation, inspection, facts, applicationVersion);
        if (reason is not null)
        {
            return Reject(operation, reason);
        }

        EngineUpgradePrecheck.TryParseMajors(operation, out var fromMajor, out var toMajor);
        var entry = new EngineUpgradeJournalEntry
        {
            Schema = EngineUpgradeConstants.JournalSchema,
            OperationId = operation.Id,
            Mode = _options.Mode.ToString(),
            FromMajor = fromMajor,
            ToMajor = toMajor,
            Old = new Dictionary<string, string>(inspection.OldState),
            Databases = facts.Databases.Select(database => database.Name).ToList(),
            AppVersion = applicationVersion!,
        };
        var context = EngineUpgradeFinisher.ContextFrom(entry, operation.ArtifactRef!, operation.ArtifactSha256!);

        try
        {
            return await RunPhasesAsync(entry, context, facts.Databases, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Engine upgrade {Id} interrupted in phase {Phase}; the startup recovery reverts it.", operation.Id, entry.Phase);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Engine upgrade {Id} failed after phase {Phase}.", operation.Id, entry.Phase);
            return await _finisher.RevertAsync(entry, context, ex.Message, cancellationToken);
        }
    }

    private async Task<UpdateExecutionResult> RunPhasesAsync(
        EngineUpgradeJournalEntry entry,
        EngineUpgradeContext context,
        IReadOnlyList<EngineDatabase> databases,
        CancellationToken cancellationToken)
    {
        await ReachAsync(entry, EngineUpgradePhase.Accepted, cancellationToken);

        await _target.PrepareArtifactAsync(context, cancellationToken);
        await ReachAsync(entry, EngineUpgradePhase.ArtifactReady, cancellationToken);

        await _target.StopApplicationAsync(cancellationToken);
        await ReachAsync(entry, EngineUpgradePhase.ApplicationStopped, cancellationToken);

        var before = await SnapshotAllAsync(entry.Databases, cancellationToken);
        await ReachAsync(entry, EngineUpgradePhase.SnapshotTaken, cancellationToken);

        var dumps = await _target.DumpAsync(context, databases, cancellationToken);
        entry.Dumps = dumps.ToList();
        await ReachAsync(entry, EngineUpgradePhase.DumpCreated, cancellationToken);

        await _target.StopOldEngineAsync(context, cancellationToken);
        await ReachAsync(entry, EngineUpgradePhase.OldEngineStopped, cancellationToken);

        await _target.ActivateNewEngineAsync(context, cancellationToken);
        _snapshotReader.ClearConnectionPools();
        await ReachAsync(entry, EngineUpgradePhase.NewEngineActive, cancellationToken);

        await _target.RestoreAsync(context, dumps, cancellationToken);
        await ReachAsync(entry, EngineUpgradePhase.Restored, cancellationToken);

        await VerifyAsync(entry, before, cancellationToken);
        await ReachAsync(entry, EngineUpgradePhase.Verified, cancellationToken);

        await _target.CommitConfigurationAsync(context, cancellationToken);
        await ReachAsync(entry, EngineUpgradePhase.ConfigurationCommitted, cancellationToken);

        await _target.StartApplicationAsync(cancellationToken);
        if (!await _healthGate.WaitForHealthyAsync(entry.AppVersion, cancellationToken))
        {
            throw new InvalidOperationException($"The application did not become healthy on PostgreSQL {entry.ToMajor}.");
        }

        await ReachAsync(entry, EngineUpgradePhase.ApplicationHealthy, cancellationToken);

        _logger.LogInformation("Engine upgrade {Id} committed: PostgreSQL {From} -> {To}.", entry.OperationId, entry.FromMajor, entry.ToMajor);
        return await _finisher.FinishAsync(
            entry,
            UpdateExecutionStatus.Succeeded,
            $"PostgreSQL upgraded from {entry.FromMajor} to {entry.ToMajor}; the old cluster is kept for a manual rollback.",
            cancellationToken);
    }

    private async Task<IReadOnlyList<EngineDatabaseSnapshot>> SnapshotAllAsync(IReadOnlyList<string> databases, CancellationToken cancellationToken)
    {
        var snapshots = new List<EngineDatabaseSnapshot>();
        foreach (var database in databases)
        {
            snapshots.Add(await _snapshotReader.SnapshotAsync(database, cancellationToken));
        }

        return snapshots;
    }

    private async Task VerifyAsync(EngineUpgradeJournalEntry entry, IReadOnlyList<EngineDatabaseSnapshot> before, CancellationToken cancellationToken)
    {
        var after = await SnapshotAllAsync(entry.Databases, cancellationToken);
        var differences = EngineSnapshotComparer.Compare(before, after);
        if (differences.Count > 0)
        {
            throw new InvalidOperationException($"Restore verification failed: {string.Join(" ", differences)}");
        }

        foreach (var database in entry.Databases)
        {
            await _snapshotReader.AnalyzeAsync(database, cancellationToken);
        }
    }

    private async Task ReachAsync(EngineUpgradeJournalEntry entry, EngineUpgradePhase phase, CancellationToken cancellationToken)
    {
        entry.Phase = phase;
        entry.Timestamps[phase.ToString()] = DateTime.UtcNow;
        await _journal.SaveAsync(entry, cancellationToken);
        _logger.LogInformation("Engine upgrade {Id} reached phase {Phase}.", entry.OperationId, phase);

        if (_options.EngineUpgradeFaultPhase == phase)
        {
            throw new InvalidOperationException($"{FaultInjectedMessage} {phase} (Updater:EngineUpgradeFaultPhase).");
        }
    }

    private UpdateExecutionResult Reject(UpdateOperation operation, string reason)
    {
        _logger.LogError("Engine upgrade {Id} rejected: {Reason}", operation.Id, reason);
        return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, reason + UpdaterConstants.NothingChangedSuffix);
    }
}
