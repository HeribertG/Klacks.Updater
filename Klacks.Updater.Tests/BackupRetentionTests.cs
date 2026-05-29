// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class BackupRetentionTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<(string, DateTime)> Backups(int count) =>
        Enumerable.Range(1, count).Select(i => ($"backup-{i}.sql", Base.AddMinutes(i))).ToList();

    [Test]
    public void Keeps_newest_n_and_deletes_the_rest()
    {
        var toDelete = BackupRetention.SelectToDelete(Backups(5), 2);

        toDelete.Count.ShouldBe(3);
        toDelete.ShouldContain("backup-1.sql");
        toDelete.ShouldContain("backup-3.sql");
        toDelete.ShouldNotContain("backup-5.sql");
        toDelete.ShouldNotContain("backup-4.sql");
    }

    [Test]
    public void Deletes_nothing_when_within_keep_count()
    {
        BackupRetention.SelectToDelete(Backups(2), 3).ShouldBeEmpty();
    }

    [Test]
    public void Keep_zero_deletes_all()
    {
        BackupRetention.SelectToDelete(Backups(3), 0).Count.ShouldBe(3);
    }

    [Test]
    public void Negative_keep_count_is_treated_as_zero()
    {
        BackupRetention.SelectToDelete(Backups(3), -5).Count.ShouldBe(3);
    }
}
