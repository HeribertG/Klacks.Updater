// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Application.EngineUpgrade;

/// <summary>
/// Contract values of the PostgreSQL engine upgrade: signature domain, journal schema and file names, space rule,
/// capability report, the retry and report intervals and the maximum age of a pending engine upgrade request.
/// </summary>
public static class EngineUpgradeConstants
{
    public const string SignatureDomain = "klacks-postgres-engine";
    public const int JournalSchema = 1;
    public const int MaxMessageLength = 1000;
    public const string JournalFileName = "journal.json";
    public const string ArchivedJournalFilePattern = "journal-{0}.json";
    public const long MinimumFreeBytes = 2L * 1024 * 1024 * 1024;
    public const int FreeSpaceDatabaseFactor = 3;
    public const int ServerVersionNumDivisor = 10000;
    public const string CapabilitySettingType = "UPDATER_CAPABILITIES";
    public const string EngineUpgradeCapability = "postgres-engine-upgrade/1";
    public static readonly TimeSpan CapabilityReportInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan CapabilityReportRetryInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan DatabaseRetryInterval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan PendingEngineUpgradeMaxAge = TimeSpan.FromMinutes(30);
    public const string ExpiredRequestMessage = "Engine upgrade request expired before the updater picked it up; start it again.";
}
