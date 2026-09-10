// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Creates and restores PostgreSQL backups via the postgres container so the pg_dump/psql client
/// version always matches the server. Backups are written to the configured directory and referenced
/// by file path in the update_history row. A restore drops and recreates the database before replaying
/// the dump: replaying into the live database left every table the failed version had created behind
/// (its migration history was reset, so the next attempt failed with "relation already exists").
/// </summary>
/// <param name="options">Deployment configuration (postgres container, user, database, backup dir)</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Klacks.Updater.Infrastructure;

public class PgDumpBackupService : IUpdateBackupService
{
    private const string DumpHeader = "PostgreSQL database dump";
    private const string CreateTableMarker = "CREATE TABLE";
    private const string MaintenanceDatabase = "postgres";

    private readonly UpdaterOptions _options;
    private readonly ILogger<PgDumpBackupService> _logger;

    public PgDumpBackupService(IOptions<UpdaterOptions> options, ILogger<PgDumpBackupService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> CreateAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.BackupDirectory);
        var fileName = $"backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sql";
        var hostPath = Path.Combine(_options.BackupDirectory, fileName);

        var dump = await ProcessRunner.RunAsync(
            "docker",
            $"exec {_options.PostgresContainer} pg_dump --clean --if-exists -U {_options.PostgresUser} -d {_options.PostgresDatabase}",
            cancellationToken: cancellationToken);

        await File.WriteAllTextAsync(hostPath, dump, cancellationToken);
        _logger.LogInformation("Created database backup at {Path}.", hostPath);
        return hostPath;
    }

    public async Task RestoreAsync(string backupRef, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupRef))
        {
            throw new FileNotFoundException($"Backup file not found: {backupRef}");
        }

        var sql = await File.ReadAllTextAsync(backupRef, cancellationToken);
        if (!IsRestorableDump(sql))
        {
            throw new InvalidOperationException($"Backup file '{backupRef}' is not a complete pg_dump; refusing to drop the database.");
        }

        _logger.LogWarning("Restoring database {Database} from {Path}: dropping and recreating it first.", _options.PostgresDatabase, backupRef);
        await ProcessRunner.RunWithStdinAsync(
            "docker",
            $"exec -i {_options.PostgresContainer} psql -v ON_ERROR_STOP=1 -U {_options.PostgresUser} -d {MaintenanceDatabase}",
            RecreateDatabaseScript(_options.PostgresDatabase, _options.PostgresUser),
            cancellationToken);

        // The forced drop killed this process's own pooled connections to the database as well;
        // discarding the pool makes the next store call open a fresh one instead of failing once.
        NpgsqlConnection.ClearAllPools();

        await ProcessRunner.RunWithStdinAsync(
            "docker",
            $"exec -i {_options.PostgresContainer} psql -U {_options.PostgresUser} -d {_options.PostgresDatabase}",
            sql,
            cancellationToken);
    }

    public static bool IsRestorableDump(string sql)
    {
        return !string.IsNullOrWhiteSpace(sql)
            && sql.Contains(DumpHeader, StringComparison.Ordinal)
            && sql.Contains(CreateTableMarker, StringComparison.Ordinal);
    }

    public static string RecreateDatabaseScript(string database, string owner)
    {
        var quotedDatabase = QuoteIdentifier(database);
        var quotedOwner = QuoteIdentifier(owner);
        return $"DROP DATABASE IF EXISTS {quotedDatabase} WITH (FORCE);\nCREATE DATABASE {quotedDatabase} OWNER {quotedOwner};\n";
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
