// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

public interface IBackupRetentionService
{
    Task PruneAsync(int keepCount, CancellationToken cancellationToken = default);
}
