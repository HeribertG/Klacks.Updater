// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Target-agnostic update/rollback state machine. For an update: optional pre-migration backup →
/// activate new image → deep health gate → commit, or roll back on failure. Rollback order is
/// strictly stop-current → restore-dump → activate-old → re-check, so no in-flight migration writes
/// into a database being restored. Never throws; always returns a terminal status.
/// </summary>
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;

namespace Klacks.Updater.Application;

public class UpdateExecutor
{
    private readonly IUpdateApplier _applier;
    private readonly IUpdateBackupService _backup;
    private readonly IUpdateHealthGate _healthGate;
    private readonly IArtifactVerifier _verifier;
    private readonly ILogger<UpdateExecutor> _logger;

    public UpdateExecutor(
        IUpdateApplier applier,
        IUpdateBackupService backup,
        IUpdateHealthGate healthGate,
        IArtifactVerifier verifier,
        ILogger<UpdateExecutor> logger)
    {
        _applier = applier;
        _backup = backup;
        _healthGate = healthGate;
        _verifier = verifier;
        _logger = logger;
    }

    public Task<UpdateExecutionResult> ExecuteAsync(UpdateOperation operation, CancellationToken cancellationToken = default)
    {
        return operation.OperationType == UpdateOperationType.Rollback
            ? RollbackToPreviousAsync(operation, operation.BackupRef, cancellationToken)
            : ExecuteUpdateAsync(operation, cancellationToken);
    }

    private async Task<UpdateExecutionResult> ExecuteUpdateAsync(UpdateOperation operation, CancellationToken cancellationToken)
    {
        if (!_verifier.Verify(operation))
        {
            _logger.LogError("Update {Id} rejected: artifact signature verification failed. Nothing was applied.", operation.Id);
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, "Artifact signature verification failed.");
        }

        string? backupRef = null;
        try
        {
            if (operation.ContainsMigrations)
            {
                backupRef = await _backup.CreateAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(operation.ArtifactRef))
            {
                throw new InvalidOperationException("Update operation has no artifact reference.");
            }

            await _applier.ActivateAsync(operation.ArtifactRef, cancellationToken);

            if (await _healthGate.WaitForHealthyAsync(cancellationToken))
            {
                return new UpdateExecutionResult(UpdateExecutionStatus.Succeeded, backupRef, "Update applied and healthy.");
            }

            _logger.LogWarning("Update {Id} did not become healthy; rolling back.", operation.Id);
            return await RollbackToPreviousAsync(operation, backupRef, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update {Id} failed during apply; rolling back.", operation.Id);
            return await RollbackToPreviousAsync(operation, backupRef, cancellationToken);
        }
    }

    private async Task<UpdateExecutionResult> RollbackToPreviousAsync(UpdateOperation operation, string? backupRef, CancellationToken cancellationToken)
    {
        try
        {
            await _applier.StopAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(backupRef))
            {
                await _backup.RestoreAsync(backupRef, cancellationToken);
            }

            await _applier.ActivateVersionAsync(operation.RollbackTargetVersion, cancellationToken);

            if (await _healthGate.WaitForHealthyAsync(cancellationToken))
            {
                return new UpdateExecutionResult(UpdateExecutionStatus.RolledBack, backupRef, "Rolled back to previous version.");
            }

            return new UpdateExecutionResult(UpdateExecutionStatus.RollbackFailed, backupRef, "Rollback completed but health check failed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback for {Id} failed.", operation.Id);
            return new UpdateExecutionResult(UpdateExecutionStatus.RollbackFailed, backupRef, $"Rollback failed: {ex.Message}");
        }
    }
}
