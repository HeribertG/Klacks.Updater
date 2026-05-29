// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

/// <summary>
/// Terminal status the executor writes back to the update_history row. Integer values match the
/// UpdateOperationStatus enum persisted by Klacks.Api so the shared table stays consistent.
/// </summary>
public enum UpdateExecutionStatus
{
    Succeeded = 2,
    Failed = 3,
    RolledBack = 4,
    RollbackFailed = 6,
}
