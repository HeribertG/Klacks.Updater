// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Creates and restores PostgreSQL backups via the postgres container so the pg_dump/psql client
/// version always matches the server. Backups are written to the configured directory and referenced
/// by file path in the update_history row.
/// </summary>
/// <param name="options">Deployment configuration (postgres container, user, database, backup dir)</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class PgDumpBackupService : IUpdateBackupService
{
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

        _logger.LogWarning("Restoring database from {Path}.", backupRef);
        var sql = await File.ReadAllTextAsync(backupRef, cancellationToken);
        await ProcessRunner.RunWithStdinAsync(
            "docker",
            $"exec -i {_options.PostgresContainer} psql -U {_options.PostgresUser} -d {_options.PostgresDatabase}",
            sql,
            cancellationToken);
    }
}
