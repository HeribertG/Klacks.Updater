// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

/// <summary>
/// Deployment-specific steps of a PostgreSQL major upgrade (dump/restore into a new cluster while the old cluster
/// stays untouched as rollback). The executor drives the ordered phases and journals OldState/NewState of the
/// context after every step; RevertToOldEngineAsync must be idempotent and work from the journal content alone.
/// </summary>
public interface IEngineUpgradeTarget
{
    Task<EngineInspection> InspectAsync(CancellationToken cancellationToken);

    Task PrepareArtifactAsync(EngineUpgradeContext context, CancellationToken cancellationToken);

    Task StopApplicationAsync(CancellationToken cancellationToken);

    Task StartApplicationAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<EngineDump>> DumpAsync(EngineUpgradeContext context, IReadOnlyList<EngineDatabase> databases, CancellationToken cancellationToken);

    Task StopOldEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken);

    Task ActivateNewEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken);

    Task RestoreAsync(EngineUpgradeContext context, IReadOnlyList<EngineDump> dumps, CancellationToken cancellationToken);

    Task CommitConfigurationAsync(EngineUpgradeContext context, CancellationToken cancellationToken);

    Task RevertToOldEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken);
}
