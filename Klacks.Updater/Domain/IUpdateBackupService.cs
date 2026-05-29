// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

public interface IUpdateBackupService
{
    Task<string> CreateAsync(CancellationToken cancellationToken = default);

    Task RestoreAsync(string backupRef, CancellationToken cancellationToken = default);
}
