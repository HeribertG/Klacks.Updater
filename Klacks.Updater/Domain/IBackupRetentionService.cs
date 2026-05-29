// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

public interface IBackupRetentionService
{
    Task PruneAsync(int keepCount, CancellationToken cancellationToken = default);
}
