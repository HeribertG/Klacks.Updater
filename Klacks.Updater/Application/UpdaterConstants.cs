// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Application;

/// <summary>
/// Timing contracts for the updater. The health-gate timeout and the stuck-lease threshold must both
/// exceed the slowest real migration (Klacks.Api compose start_period is 600s) so the gate never
/// declares failure mid-migration and stuck-recovery never reclaims the updater's own in-flight work.
/// </summary>
public static class UpdaterConstants
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan HealthGateTimeout = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan HealthPollInterval = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan StuckLeaseTimeout = TimeSpan.FromMinutes(30);

    public const int DefaultBackupRetentionCount = 3;

    public static readonly TimeSpan WhisperReadyTimeout = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan WhisperModelInstallTimeout = TimeSpan.FromMinutes(25);
    public const string NativeUpdaterServiceName = "KlacksUpdater";
    public const string NativeDataRootKey = "Hosting:DataRoot";
    public const string NativeConfigDirectoryName = "config";
    public const string NativeConfigFileName = "klacks.native.json";
    public const string VersionPath = "/api/Version";
    public static readonly TimeSpan BundleDownloadTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ServiceStateTimeout = TimeSpan.FromMinutes(2);
    // Klacks reports "start pending" while it migrates the database and initialises before it accepts requests.
    public static readonly TimeSpan ServiceStartTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan ServiceStatePollInterval = TimeSpan.FromSeconds(1);
}
