// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Infrastructure;
using NUnit.Framework;
using Shouldly;

[TestFixture]
[Platform("Win")]
public class JunctionLinkSwitcherTests
{
    private string _root = string.Empty;
    private string _current = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "klacks-junction-" + Guid.NewGuid().ToString("N"));
        _current = Path.Combine(_root, "current");
        Directory.CreateDirectory(Path.Combine(_root, "versions", "1.0.0"));
        Directory.CreateDirectory(Path.Combine(_root, "versions", "1.1.0"));
        File.WriteAllText(Path.Combine(_root, "versions", "1.1.0", "marker.txt"), "1.1.0");
    }

    [TearDown]
    public void TearDown()
    {
        var current = new DirectoryInfo(_current);
        if (current.Exists)
        {
            current.Delete();
        }

        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task Switch_creates_and_then_flips_the_current_junction()
    {
        var switcher = new JunctionLinkSwitcher();

        await switcher.SwitchAsync(_current, Path.Combine(_root, "versions", "1.0.0"));
        switcher.ReadTarget(_current).ShouldBe(Path.Combine(_root, "versions", "1.0.0"));

        await switcher.SwitchAsync(_current, Path.Combine(_root, "versions", "1.1.0"));
        switcher.ReadTarget(_current).ShouldBe(Path.Combine(_root, "versions", "1.1.0"));
        File.ReadAllText(Path.Combine(_current, "marker.txt")).ShouldBe("1.1.0");
        Directory.Exists(Path.Combine(_root, "versions", "1.0.0")).ShouldBeTrue();
    }

    [Test]
    public async Task Switch_refuses_to_replace_a_real_directory()
    {
        Directory.CreateDirectory(_current);

        await Should.ThrowAsync<InvalidOperationException>(
            () => new JunctionLinkSwitcher().SwitchAsync(_current, Path.Combine(_root, "versions", "1.1.0")));
    }
}
