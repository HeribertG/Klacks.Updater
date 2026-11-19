// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

/// <summary>
/// Target-neutral read access to the cluster the updater is connected to: prechecks, content snapshots for the
/// verification of a restore, and ANALYZE after a restore. ClearConnectionPools drops every pooled connection after
/// the engine behind the connection string has been switched.
/// </summary>
public interface IEngineSnapshotReader
{
    Task<EngineClusterFacts> ReadClusterFactsAsync(CancellationToken cancellationToken);

    Task<EngineDatabaseSnapshot> SnapshotAsync(string database, CancellationToken cancellationToken);

    Task AnalyzeAsync(string database, CancellationToken cancellationToken);

    void ClearConnectionPools();
}
