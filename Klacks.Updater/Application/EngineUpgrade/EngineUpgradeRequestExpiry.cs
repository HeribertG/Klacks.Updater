// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Decides whether a claimed PostgreSQL engine upgrade request waited too long in the queue to be executed. The
/// administrator starts the upgrade expecting it to run now; a request older than PendingEngineUpgradeMaxAge (for example
/// queued while the updater was stopped) is failed instead of disconnecting everyone at an unexpected time.
/// </summary>
using Klacks.Updater.Domain;

namespace Klacks.Updater.Application.EngineUpgrade;

public static class EngineUpgradeRequestExpiry
{
    public static bool IsExpired(UpdateOperation operation, DateTime nowUtc) =>
        operation.OperationType == UpdateOperationType.PostgresUpgrade
        && nowUtc - operation.RequestedAt > EngineUpgradeConstants.PendingEngineUpgradeMaxAge;
}
