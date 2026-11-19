// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain.EngineUpgrade;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class EngineSnapshotComparerTests
{
    private static readonly EngineDatabaseSnapshot Before = new(
        "Klacks",
        new Dictionary<string, long> { ["public.clients"] = 3, ["public.\"Weird Table\""] = 0 },
        ["pg_trgm", "plpgsql", "vector"],
        "20261119_Init",
        []);

    [Test]
    public void Identical_content_is_verified() =>
        EngineSnapshotComparer.Compare([Before], [Before with { }]).ShouldBeEmpty();

    [Test]
    public void Extra_extension_after_restore_is_accepted() =>
        EngineSnapshotComparer.Compare([Before], [Before with { Extensions = ["pg_trgm", "plpgsql", "vector", "extra"] }]).ShouldBeEmpty();

    [Test]
    public void Missing_database_table_row_extension_migration_or_invalid_index_is_reported()
    {
        EngineSnapshotComparer.Compare([Before], []).Count.ShouldBe(1);
        EngineSnapshotComparer.Compare([Before], [Before with { RowCounts = new Dictionary<string, long> { ["public.clients"] = 3 } }]).Count.ShouldBe(1);
        EngineSnapshotComparer.Compare([Before], [Before with { RowCounts = new Dictionary<string, long> { ["public.clients"] = 2, ["public.\"Weird Table\""] = 0 } }]).Count.ShouldBe(1);
        EngineSnapshotComparer.Compare([Before], [Before with { Extensions = ["pg_trgm", "plpgsql"] }]).Count.ShouldBe(1);
        EngineSnapshotComparer.Compare([Before], [Before with { MaxMigrationId = null }]).Count.ShouldBe(1);
        EngineSnapshotComparer.Compare([Before], [Before with { InvalidIndexes = ["public.knowledge_index_embedding_idx"] }]).Count.ShouldBe(1);
    }

    [Test]
    public void Unexpected_table_after_restore_is_reported() =>
        EngineSnapshotComparer.Compare(
            [Before],
            [Before with { RowCounts = new Dictionary<string, long>(Before.RowCounts) { ["public.extra"] = 0 } }])
            .Count.ShouldBe(1);
}
