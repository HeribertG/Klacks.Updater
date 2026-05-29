// Copyright (c) Heribert Gasparoli Private. All rights reserved.

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
}
