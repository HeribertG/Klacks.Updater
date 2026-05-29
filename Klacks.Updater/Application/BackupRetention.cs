// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Pure selection logic for backup retention: given backups with timestamps and a keep-count, returns
/// the ones to delete (all but the newest N). Separated from the filesystem so it is unit-testable.
/// </summary>
namespace Klacks.Updater.Application;

public static class BackupRetention
{
    public static IReadOnlyList<string> SelectToDelete(IReadOnlyList<(string Path, DateTime Timestamp)> backups, int keepCount)
    {
        if (keepCount < 0)
        {
            keepCount = 0;
        }

        return backups
            .OrderByDescending(b => b.Timestamp)
            .Skip(keepCount)
            .Select(b => b.Path)
            .ToList();
    }
}
