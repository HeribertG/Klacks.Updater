// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Points the stable "current" path of a native installation at one version directory (junction on Windows,
/// symbolic link elsewhere), so the service definition never changes between versions.
/// </summary>
public interface IVersionLinkSwitcher
{
    Task SwitchAsync(string linkPath, string targetDirectory, CancellationToken cancellationToken = default);

    string? ReadTarget(string linkPath);
}
