// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.Runtime.Versioning;
using Klacks.Updater.Application;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsWhisperResourceCheckTests
{
    private UpdaterOptions _options = new();

    private Dictionary<string, string?> _configuration = [];

    [SetUp]
    public void SetUp()
    {
        _options = new UpdaterOptions { NativeProgramRoot = Path.GetTempPath() };
        _configuration = [];
    }

    private WindowsWhisperResourceCheck CreateCheck() =>
        new(
            Options.Create(_options),
            new ConfigurationBuilder().AddInMemoryCollection(_configuration).Build(),
            NullLogger<WindowsWhisperResourceCheck>.Instance);

    [Test]
    public async Task Check_accepts_trivial_requirements()
    {
        (await CreateCheck().CheckAsync(1, 1)).ShouldBeNull();
    }

    [Test]
    public async Task Check_rejects_impossible_memory_requirement()
    {
        (await CreateCheck().CheckAsync(long.MaxValue, 1))!.ShouldStartWith("Not enough free memory");
    }

    [Test]
    public async Task Check_rejects_impossible_disk_requirement()
    {
        (await CreateCheck().CheckAsync(1, long.MaxValue))!.ShouldStartWith("Not enough free disk space");
    }

    [Test]
    public async Task Check_skips_disk_probe_without_program_root_and_data_root()
    {
        _options.NativeProgramRoot = string.Empty;

        (await CreateCheck().CheckAsync(1, long.MaxValue)).ShouldBeNull();
    }

    [Test]
    public async Task Check_probes_the_data_root_when_the_program_root_is_unknown()
    {
        _options.NativeProgramRoot = string.Empty;
        _configuration[UpdaterConstants.NativeDataRootKey] = Path.GetTempPath();

        (await CreateCheck().CheckAsync(1, long.MaxValue))!.ShouldStartWith("Not enough free disk space");
    }
}
