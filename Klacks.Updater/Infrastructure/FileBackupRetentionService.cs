// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Prunes the backup directory to the newest keep-count files using the pure BackupRetention logic.
/// Deletion failures are logged, not thrown, so retention housekeeping never fails an update.
/// </summary>
/// <param name="options">Deployment configuration (backup directory)</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class FileBackupRetentionService : IBackupRetentionService
{
    private const string BackupPattern = "backup-*.sql";

    private readonly UpdaterOptions _options;
    private readonly ILogger<FileBackupRetentionService> _logger;

    public FileBackupRetentionService(IOptions<UpdaterOptions> options, ILogger<FileBackupRetentionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task PruneAsync(int keepCount, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_options.BackupDirectory))
        {
            return Task.CompletedTask;
        }

        var backups = Directory
            .GetFiles(_options.BackupDirectory, BackupPattern)
            .Select(path => (Path: path, Timestamp: File.GetLastWriteTimeUtc(path)))
            .ToList();

        foreach (var path in BackupRetention.SelectToDelete(backups, keepCount))
        {
            try
            {
                File.Delete(path);
                _logger.LogInformation("Pruned old backup {Path}.", path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to prune backup {Path}.", path);
            }
        }

        return Task.CompletedTask;
    }
}
