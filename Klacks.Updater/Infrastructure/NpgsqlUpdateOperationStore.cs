// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Raw-Npgsql access to the shared update_history table (no EF / no Klacks.Api dependency).
/// Claiming is a single atomic statement (FOR UPDATE SKIP LOCKED) so overlapping updater restarts
/// cannot double-claim. Stuck Running rows past the lease are marked Failed so the queue is never
/// jammed forever; recovery of the affected system is then the admin's first-class manual rollback.
/// </summary>
/// <param name="options">Deployment configuration (connection string)</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Klacks.Updater.Infrastructure;

public class NpgsqlUpdateOperationStore : IUpdateOperationStore
{
    private const int StatusPending = 0;
    private const int StatusRunning = 1;
    private const int StatusFailed = 3;

    private readonly UpdaterOptions _options;

    public NpgsqlUpdateOperationStore(IOptions<UpdaterOptions> options)
    {
        _options = options.Value;
    }

    public async Task<UpdateOperation?> ClaimNextPendingAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE update_history SET status = @running, started_at = now(), last_heartbeat_at = now(), update_time = now()
            WHERE id = (
                SELECT id FROM update_history
                WHERE status = @pending AND is_deleted = false
                ORDER BY requested_at
                LIMIT 1 FOR UPDATE SKIP LOCKED
            )
            RETURNING id, operation_type, from_version, target_version, artifact_ref, artifact_sha256, artifact_signature, contains_migrations, backup_ref;";

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("running", StatusRunning);
        command.Parameters.AddWithValue("pending", StatusPending);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new UpdateOperation
        {
            Id = reader.GetGuid(0),
            OperationType = (UpdateOperationType)reader.GetInt32(1),
            FromVersion = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            TargetVersion = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            ArtifactRef = reader.IsDBNull(4) ? null : reader.GetString(4),
            ArtifactSha256 = reader.IsDBNull(5) ? null : reader.GetString(5),
            ArtifactSignature = reader.IsDBNull(6) ? null : reader.GetString(6),
            ContainsMigrations = reader.GetBoolean(7),
            BackupRef = reader.IsDBNull(8) ? null : reader.GetString(8),
        };
    }

    public async Task HeartbeatAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE update_history SET last_heartbeat_at = now() WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteAsync(Guid id, UpdateExecutionStatus status, string message, string? backupRef, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE update_history SET status = @status, completed_at = now(), message = @message, backup_ref = @backupRef, update_time = now() WHERE id = @id;",
            connection);
        command.Parameters.AddWithValue("status", (int)status);
        command.Parameters.AddWithValue("message", message);
        command.Parameters.AddWithValue("backupRef", (object?)backupRef ?? DBNull.Value);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ReclaimStuckAsync(TimeSpan leaseTimeout, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE update_history SET status = @failed, completed_at = now(), message = 'Reclaimed: updater lease expired', update_time = now() WHERE status = @running AND last_heartbeat_at < @cutoff;",
            connection);
        command.Parameters.AddWithValue("failed", StatusFailed);
        command.Parameters.AddWithValue("running", StatusRunning);
        command.Parameters.AddWithValue("cutoff", DateTime.UtcNow.Subtract(leaseTimeout));
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> GetBackupRetentionCountAsync(int defaultCount, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT value FROM settings WHERE type = 'UPDATE_BACKUP_RETENTION_COUNT' LIMIT 1;", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string s && int.TryParse(s, out var count) && count >= 0 ? count : defaultCount;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
