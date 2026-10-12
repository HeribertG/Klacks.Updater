// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Native counterpart of PgDumpBackupService: runs the pg_dump/psql binaries installed next to the bundled
/// PostgreSQL instead of exec-ing into a container. Connection details come from Updater:ConnectionString and are
/// passed through the libpq environment (PGHOST, PGPASSWORD, ...) so the password never appears on a command line.
/// Dump format and the drop-and-recreate restore are identical to the Docker variant.
/// </summary>
/// <param name="options">Updater configuration (connection string, PostgreSQL bin directory, backup directory)</param>
/// <param name="logger">Logger for diagnostic output</param>

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Klacks.Updater.Infrastructure;

public class NativePgDumpBackupService : IUpdateBackupService
{
    private const string PgDumpExecutable = "pg_dump";
    private const string PsqlExecutable = "psql";
    private const string MaintenanceDatabase = "postgres";
    private const string BackupFilePattern = "backup-{0:yyyyMMdd-HHmmss}.sql";

    private readonly UpdaterOptions _options;
    private readonly NpgsqlConnectionStringBuilder _connection;
    private readonly ILogger<NativePgDumpBackupService> _logger;

    public NativePgDumpBackupService(IOptions<UpdaterOptions> options, ILogger<NativePgDumpBackupService> logger)
    {
        _options = options.Value;
        _connection = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
        _logger = logger;
    }

    public async Task<string> CreateAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.BackupDirectory);
        var path = Path.Combine(_options.BackupDirectory, string.Format(BackupFilePattern, DateTime.UtcNow));

        await ProcessRunner.RunAsync(
            Executable(PgDumpExecutable),
            $"--clean --if-exists --file \"{path}\" --dbname {Database}",
            environment: LibpqEnvironment(),
            cancellationToken: cancellationToken);

        _logger.LogInformation("Created database backup at {Path}.", path);
        return path;
    }

    public async Task RestoreAsync(string backupRef, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupRef))
        {
            throw new FileNotFoundException($"Backup file not found: {backupRef}");
        }

        var sql = await File.ReadAllTextAsync(backupRef, cancellationToken);
        if (!PgDumpBackupService.IsRestorableDump(sql))
        {
            throw new InvalidOperationException($"Backup file '{backupRef}' is not a complete pg_dump; refusing to drop the database.");
        }

        _logger.LogWarning("Restoring database {Database} from {Path}: dropping and recreating it first.", Database, backupRef);
        await ProcessRunner.RunWithStdinAsync(
            Executable(PsqlExecutable),
            $"-v ON_ERROR_STOP=1 --dbname {MaintenanceDatabase}",
            PgDumpBackupService.RecreateDatabaseScript(Database, User),
            cancellationToken,
            LibpqEnvironment());

        NpgsqlConnection.ClearAllPools();

        await ProcessRunner.RunAsync(
            Executable(PsqlExecutable),
            $"--file \"{backupRef}\" --dbname {Database}",
            environment: LibpqEnvironment(),
            cancellationToken: cancellationToken);
    }

    private string Database => _connection.Database ?? string.Empty;

    private string User => _connection.Username ?? string.Empty;

    private string Executable(string name)
    {
        return string.IsNullOrWhiteSpace(_options.PostgresBinDirectory)
            ? name
            : Path.Combine(_options.PostgresBinDirectory, name);
    }

    private Dictionary<string, string> LibpqEnvironment()
    {
        return new Dictionary<string, string>
        {
            ["PGHOST"] = _connection.Host ?? string.Empty,
            ["PGPORT"] = _connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["PGUSER"] = User,
            ["PGPASSWORD"] = _connection.Password ?? string.Empty,
        };
    }
}
