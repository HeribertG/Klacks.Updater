// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Test double for IEngineSnapshotReader: fixed cluster facts, a snapshot that switches to AfterRestore once
/// IsRestored reports true, and counters for ANALYZE and pool clearing.
/// </summary>
namespace Klacks.Updater.Tests;

using Klacks.Updater.Domain.EngineUpgrade;

public sealed class FakeEngineSnapshotReader : IEngineSnapshotReader
{
    public EngineClusterFacts Facts { get; set; } = new(
        17,
        [
            new EngineDatabase("Klacks", "admin", "UTF8", "en_US.utf8", "en_US.utf8", "libc", null),
            new EngineDatabase("klacks_diag", "admin", "UTF8", "en_US.utf8", "en_US.utf8", "libc", null),
        ],
        [],
        1024);

    public Func<bool> IsRestored { get; set; } = () => false;

    public Func<string, EngineDatabaseSnapshot>? AfterRestore { get; set; }

    public List<string> Analyzed { get; } = [];

    public int PoolClears { get; private set; }

    public static EngineDatabaseSnapshot Snapshot(string database) =>
        new(database, new Dictionary<string, long> { ["public.clients"] = 42 }, ["pg_trgm", "vector"], "20261119_Init", []);

    public Task<EngineClusterFacts> ReadClusterFactsAsync(CancellationToken cancellationToken) => Task.FromResult(Facts);

    public Task<EngineDatabaseSnapshot> SnapshotAsync(string database, CancellationToken cancellationToken) =>
        Task.FromResult(IsRestored() && AfterRestore is not null ? AfterRestore(database) : Snapshot(database));

    public Task AnalyzeAsync(string database, CancellationToken cancellationToken)
    {
        Analyzed.Add(database);
        return Task.CompletedTask;
    }

    public void ClearConnectionPools() => PoolClears++;
}
