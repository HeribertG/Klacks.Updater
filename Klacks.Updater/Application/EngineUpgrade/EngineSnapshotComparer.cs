// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Pure comparison of the content snapshots taken before the dump and after the restore. The restore is accepted
/// only when every database has the same tables with exactly the same row counts, the same highest EF migration,
/// every extension of the old cluster (by name; the new engine may ship a newer extension version) and no invalid
/// index. Returns the differences; an empty list means verified.
/// </summary>
using Klacks.Updater.Domain.EngineUpgrade;

namespace Klacks.Updater.Application.EngineUpgrade;

public static class EngineSnapshotComparer
{
    public static IReadOnlyList<string> Compare(IReadOnlyList<EngineDatabaseSnapshot> before, IReadOnlyList<EngineDatabaseSnapshot> after)
    {
        var differences = new List<string>();
        var afterByName = after.ToDictionary(snapshot => snapshot.Database, StringComparer.Ordinal);

        foreach (var old in before)
        {
            if (!afterByName.TryGetValue(old.Database, out var restored))
            {
                differences.Add($"Database {old.Database} is missing after the restore.");
                continue;
            }

            CompareDatabase(old, restored, differences);
        }

        return differences;
    }

    private static void CompareDatabase(EngineDatabaseSnapshot old, EngineDatabaseSnapshot restored, List<string> differences)
    {
        foreach (var (table, count) in old.RowCounts)
        {
            if (!restored.RowCounts.TryGetValue(table, out var restoredCount))
            {
                differences.Add($"{old.Database}: table {table} is missing.");
            }
            else if (restoredCount != count)
            {
                differences.Add($"{old.Database}: table {table} has {restoredCount} rows instead of {count}.");
            }
        }

        foreach (var table in restored.RowCounts.Keys.Except(old.RowCounts.Keys, StringComparer.Ordinal))
        {
            differences.Add($"{old.Database}: unexpected table {table}.");
        }

        foreach (var extension in old.Extensions.Except(restored.Extensions, StringComparer.Ordinal))
        {
            differences.Add($"{old.Database}: extension {extension} is missing.");
        }

        if (!string.Equals(old.MaxMigrationId, restored.MaxMigrationId, StringComparison.Ordinal))
        {
            differences.Add($"{old.Database}: latest migration is {restored.MaxMigrationId ?? "none"} instead of {old.MaxMigrationId ?? "none"}.");
        }

        foreach (var index in restored.InvalidIndexes)
        {
            differences.Add($"{old.Database}: index {index} is invalid.");
        }
    }
}
