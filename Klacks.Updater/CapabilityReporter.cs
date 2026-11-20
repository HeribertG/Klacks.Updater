// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Upserts the UPDATER_CAPABILITIES setting at start and every 15 minutes (the nightly reset of the playground deletes
/// it). "postgres-engine-upgrade/1" is reported only while the registered engine upgrade target inspects as
/// supported. The journal is checked before the (slow) inspection and again right before the write, so an upgrade that
/// starts meanwhile suppresses the report; the remaining window (one database round trip) closes long before the upgrade
/// takes its snapshot, which follows the artifact pull and the application stop. Failures (database down) are logged and
/// retried at the next tick.
/// </summary>
/// <param name="store">settings access</param>
/// <param name="target">Engine upgrade target of this deployment</param>
/// <param name="journal">Engine upgrade journal; an existing journal pauses the report</param>
/// <param name="options">Updater configuration (mode)</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.Reflection;
using Klacks.Updater.Application;
using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using Microsoft.Extensions.Options;

namespace Klacks.Updater;

public class CapabilityReporter : BackgroundService
{
    private const string UnknownVersion = "unknown";

    private readonly IUpdateOperationStore _store;
    private readonly IEngineUpgradeTarget _target;
    private readonly IEngineUpgradeJournal _journal;
    private readonly UpdaterOptions _options;
    private readonly ILogger<CapabilityReporter> _logger;

    public CapabilityReporter(
        IUpdateOperationStore store,
        IEngineUpgradeTarget target,
        IEngineUpgradeJournal journal,
        IOptions<UpdaterOptions> options,
        ILogger<CapabilityReporter> logger)
    {
        _store = store;
        _target = target;
        _journal = journal;
        _options = options.Value;
        _logger = logger;
    }

    public static string UpdaterVersion =>
        typeof(CapabilityReporter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? UnknownVersion;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ReportOnceAsync(stoppingToken);
            await Task.Delay(EngineUpgradeConstants.CapabilityReportInterval, stoppingToken);
        }
    }

    public async Task ReportOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_journal.Exists())
            {
                return;
            }

            var inspection = await _target.InspectAsync(cancellationToken);
            IReadOnlyList<string> capabilities = inspection.UnsupportedReason is null
                ? [EngineUpgradeConstants.EngineUpgradeCapability]
                : [];
            var value = UpdaterCapabilityReport.Build(UpdaterVersion, _options.Mode, capabilities, DateTime.UtcNow);
            if (_journal.Exists())
            {
                return;
            }

            await _store.UpsertSettingAsync(EngineUpgradeConstants.CapabilitySettingType, value, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Reporting the updater capabilities failed; retrying at the next interval.");
        }
    }
}
