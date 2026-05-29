// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

/// <summary>
/// Hand-off queue access against the shared update_history table. Claiming is atomic (single SQL
/// statement with FOR UPDATE SKIP LOCKED) so overlapping updater restarts cannot double-claim.
/// </summary>
public interface IUpdateOperationStore
{
    Task<UpdateOperation?> ClaimNextPendingAsync(CancellationToken cancellationToken = default);

    Task HeartbeatAsync(Guid id, CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid id, UpdateExecutionStatus status, string message, string? backupRef, CancellationToken cancellationToken = default);

    Task<int> ReclaimStuckAsync(TimeSpan leaseTimeout, CancellationToken cancellationToken = default);

    Task<int> GetBackupRetentionCountAsync(int defaultCount, CancellationToken cancellationToken = default);
}
