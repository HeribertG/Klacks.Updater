// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

/// <summary>
/// Content fingerprint of one database, taken before the dump and after the restore to prove nothing was lost.
/// </summary>
/// <param name="Database">Database name</param>
/// <param name="RowCounts">Exact row count per user table, keyed by the quoted schema-qualified table name</param>
/// <param name="Extensions">Installed extension names</param>
/// <param name="MaxMigrationId">Highest __EFMigrationsHistory id, or null when the database has no such table</param>
/// <param name="InvalidIndexes">Indexes with indisvalid = false</param>
public record EngineDatabaseSnapshot(
    string Database,
    IReadOnlyDictionary<string, long> RowCounts,
    IReadOnlyList<string> Extensions,
    string? MaxMigrationId,
    IReadOnlyList<string> InvalidIndexes);
