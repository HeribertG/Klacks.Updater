// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Runs a PostgreSQL engine upgrade operation (type PostgresUpgrade). This build only inspects the deployment and
/// fails the operation without any change; the dump/restore state machine follows in a later step. A failing
/// inspection is reported as Failed, never as RollbackFailed, because nothing has been touched at that point.
/// </summary>
/// <param name="target">Deployment-specific engine upgrade steps</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using Microsoft.Extensions.Logging;

namespace Klacks.Updater.Application.EngineUpgrade;

public class EngineUpgradeExecutor
{
    public const string NotAvailableMessage = "PostgreSQL engine upgrade is not available in this updater build.";
    public const string InspectionFailedMessage = "Inspecting the PostgreSQL engine failed.";

    private readonly IEngineUpgradeTarget _target;
    private readonly ILogger<EngineUpgradeExecutor> _logger;

    public EngineUpgradeExecutor(IEngineUpgradeTarget target, ILogger<EngineUpgradeExecutor> logger)
    {
        _target = target;
        _logger = logger;
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(UpdateOperation operation, CancellationToken cancellationToken = default)
    {
        EngineInspection inspection;
        try
        {
            inspection = await _target.InspectAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Engine upgrade {Id}: inspection failed.", operation.Id);
            return Reject(operation, InspectionFailedMessage);
        }

        return Reject(operation, inspection.UnsupportedReason ?? NotAvailableMessage);
    }

    private UpdateExecutionResult Reject(UpdateOperation operation, string reason)
    {
        _logger.LogError("Engine upgrade {Id} rejected: {Reason}", operation.Id, reason);
        return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, reason + UpdaterConstants.NothingChangedSuffix);
    }
}
