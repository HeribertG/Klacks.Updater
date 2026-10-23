// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.Runtime.Versioning;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using NUnit.Framework;
using Shouldly;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsUninstallEntryVersionRecorderTests
{
    private const string TestRootPath = @"SOFTWARE\Klacks.Updater.Tests";

    private string _testKeyPath = string.Empty;
    private string _entryPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _testKeyPath = TestRootPath + @"\" + Guid.NewGuid().ToString("N");
        _entryPath = _testKeyPath + @"\" + WindowsUninstallEntry.AppId + WindowsUninstallEntry.InnoSetupKeySuffix;
    }

    [TearDown]
    public void TearDown()
    {
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        baseKey.DeleteSubKeyTree(_testKeyPath, throwOnMissingSubKey: false);
        using var root = baseKey.OpenSubKey(TestRootPath);
        if (root is not null && root.SubKeyCount == 0 && root.ValueCount == 0)
        {
            baseKey.DeleteSubKey(TestRootPath, throwOnMissingSubKey: false);
        }
    }

    private WindowsUninstallEntryVersionRecorder CreateRecorder() =>
        new(RegistryHive.CurrentUser, _entryPath, NullLogger<WindowsUninstallEntryVersionRecorder>.Instance);

    [Test]
    public void Record_overwrites_DisplayVersion_of_an_existing_entry_and_keeps_other_values()
    {
        using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
        using (var entry = baseKey.CreateSubKey(_entryPath))
        {
            entry.SetValue(WindowsUninstallEntry.DisplayVersionValueName, "1.0.48");
            entry.SetValue("DisplayName", "Klacks");
        }

        CreateRecorder().Record("1.0.50");

        using var readBase = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var written = readBase.OpenSubKey(_entryPath);
        written.ShouldNotBeNull();
        written.GetValue(WindowsUninstallEntry.DisplayVersionValueName).ShouldBe("1.0.50");
        written.GetValueKind(WindowsUninstallEntry.DisplayVersionValueName).ShouldBe(RegistryValueKind.String);
        written.GetValue("DisplayName").ShouldBe("Klacks");
    }

    [Test]
    public void Record_never_creates_a_missing_entry()
    {
        CreateRecorder().Record("1.0.50");

        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var entry = baseKey.OpenSubKey(_entryPath);
        entry.ShouldBeNull();
    }

    [Test]
    public void Container_resolves_the_production_constructor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IInstalledVersionRecorder, WindowsUninstallEntryVersionRecorder>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        provider.GetRequiredService<IInstalledVersionRecorder>().ShouldBeOfType<WindowsUninstallEntryVersionRecorder>();
    }

    [Test]
    public void Record_rejects_an_empty_version()
    {
        Should.Throw<ArgumentException>(() => CreateRecorder().Record(" "));
    }
}
