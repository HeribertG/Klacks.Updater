// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Reads the version the running Klacks API reports, as major.minor.patch, or null when it is not reachable.
/// </summary>
public interface IApplicationVersionReader
{
    Task<string?> ReadRunningVersionAsync(CancellationToken cancellationToken);
}
