// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Writes the running version into "DisplayVersion" of the setup program's uninstall entry, always through the
/// 64-bit registry view (the setup installs in 64-bit mode, so the updater must not land in WOW6432Node). The key is
/// opened, never created: a ZIP installation has no entry and is skipped. Registry errors propagate to the caller.
/// </summary>
/// <param name="hive">Registry hive of the entry (LocalMachine in production; tests use CurrentUser)</param>
/// <param name="subKeyPath">Path of the uninstall entry below the hive</param>
/// <param name="logger">Logger for diagnostic output</param>

using System.Runtime.Versioning;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Klacks.Updater.Infrastructure;

[SupportedOSPlatform("windows")]
public class WindowsUninstallEntryVersionRecorder : IInstalledVersionRecorder
{
    private readonly RegistryHive _hive;
    private readonly string _subKeyPath;
    private readonly ILogger<WindowsUninstallEntryVersionRecorder> _logger;

    public WindowsUninstallEntryVersionRecorder(ILogger<WindowsUninstallEntryVersionRecorder> logger)
        : this(RegistryHive.LocalMachine, WindowsUninstallEntry.SubKeyPath, logger)
    {
    }

    public WindowsUninstallEntryVersionRecorder(
        RegistryHive hive, string subKeyPath, ILogger<WindowsUninstallEntryVersionRecorder> logger)
    {
        _hive = hive;
        _subKeyPath = subKeyPath;
        _logger = logger;
    }

    public void Record(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("The version to record must not be empty.", nameof(version));
        }

        using var baseKey = RegistryKey.OpenBaseKey(_hive, RegistryView.Registry64);
        using var entry = baseKey.OpenSubKey(_subKeyPath, writable: true);
        if (entry is null)
        {
            _logger.LogInformation("No setup uninstall entry at {Key}; installed version {Version} not recorded.", _subKeyPath, version);
            return;
        }

        entry.SetValue(WindowsUninstallEntry.DisplayVersionValueName, version, RegistryValueKind.String);
        _logger.LogInformation("Recorded installed version {Version} in {Key}.", version, _subKeyPath);
    }
}
