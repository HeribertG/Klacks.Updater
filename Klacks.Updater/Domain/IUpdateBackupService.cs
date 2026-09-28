// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

public interface IUpdateBackupService
{
    Task<string> CreateAsync(CancellationToken cancellationToken = default);

    Task RestoreAsync(string backupRef, CancellationToken cancellationToken = default);
}
