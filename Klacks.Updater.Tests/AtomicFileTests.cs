// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Infrastructure;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class AtomicFileTests
{
    private string _directory = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "klacks-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public async Task WriteAllLinesAsync_creates_a_missing_file_and_leaves_no_temporary_file()
    {
        var path = Path.Combine(_directory, ".env");

        await AtomicFile.WriteAllLinesAsync(path, ["A=1", "B=2"]);

        (await File.ReadAllLinesAsync(path)).ShouldBe(["A=1", "B=2"]);
        Directory.GetFiles(_directory).Select(Path.GetFileName).ShouldBe([".env"]);
    }

    [Test]
    public async Task WriteAllLinesAsync_replaces_the_existing_content_completely()
    {
        var path = Path.Combine(_directory, ".env");
        await File.WriteAllLinesAsync(path, ["OLD=1", "OLD=2", "OLD=3"]);

        await AtomicFile.WriteAllLinesAsync(path, ["NEW=1"]);

        (await File.ReadAllLinesAsync(path)).ShouldBe(["NEW=1"]);
        Directory.GetFiles(_directory).Length.ShouldBe(1);
    }

    [Test]
    [Platform(Exclude = "Win")]
    public async Task WriteAllLinesAsync_keeps_the_unix_permissions_of_the_existing_file()
    {
        var path = Path.Combine(_directory, ".env");
        await File.WriteAllLinesAsync(path, ["SECRET=1"]);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await AtomicFile.WriteAllLinesAsync(path, ["SECRET=2"]);

        File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Test]
    [Platform(Exclude = "Win")]
    public async Task WriteAllLinesAsync_creates_a_new_file_owner_only()
    {
        var path = Path.Combine(_directory, ".env");

        await AtomicFile.WriteAllLinesAsync(path, ["SECRET=1"]);

        File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Test]
    [Platform(Exclude = "Win")]
    public async Task WriteAllLinesAsync_does_not_reuse_a_world_readable_leftover_temporary_file()
    {
        var path = Path.Combine(_directory, ".env");
        var leftover = path + ".updater-tmp";
        await File.WriteAllLinesAsync(leftover, ["STALE=1"]);
        File.SetUnixFileMode(leftover, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        await AtomicFile.WriteAllLinesAsync(path, ["SECRET=1"]);

        File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        (await File.ReadAllLinesAsync(path)).ShouldBe(["SECRET=1"]);
    }
}
