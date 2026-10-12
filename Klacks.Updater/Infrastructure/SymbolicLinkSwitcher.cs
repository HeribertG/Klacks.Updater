// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Link switcher for macOS and Linux: creates the new symbolic link under a temporary name and renames it over
/// "current", which rename(2) does atomically.
/// </summary>

namespace Klacks.Updater.Infrastructure;

public class SymbolicLinkSwitcher : Domain.IVersionLinkSwitcher
{
    private const string PendingSuffix = ".pending";

    public Task SwitchAsync(string linkPath, string targetDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(targetDirectory))
        {
            throw new DirectoryNotFoundException($"Version directory not found: {targetDirectory}");
        }

        var existing = new FileInfo(linkPath);
        if (Directory.Exists(linkPath) && new DirectoryInfo(linkPath).LinkTarget is null)
        {
            throw new InvalidOperationException($"'{linkPath}' is a real directory, not a link; refusing to replace it.");
        }

        var pending = linkPath + PendingSuffix;
        File.Delete(pending);
        File.CreateSymbolicLink(pending, targetDirectory);
        File.Move(pending, existing.FullName, overwrite: true);
        return Task.CompletedTask;
    }

    public string? ReadTarget(string linkPath)
    {
        var info = new FileInfo(linkPath);
        return info.LinkTarget;
    }
}
