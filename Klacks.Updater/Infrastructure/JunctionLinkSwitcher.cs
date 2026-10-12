// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Windows link switcher based on directory junctions, which unlike symbolic links need no administrator right or
/// developer mode. The new junction is created next to the old one and renamed over it, so "current" is missing for
/// at most one rename while the service is stopped.
/// </summary>

namespace Klacks.Updater.Infrastructure;

public class JunctionLinkSwitcher : Domain.IVersionLinkSwitcher
{
    private const string CommandShell = "cmd.exe";
    private const string PendingSuffix = ".pending";

    public async Task SwitchAsync(string linkPath, string targetDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(targetDirectory))
        {
            throw new DirectoryNotFoundException($"Version directory not found: {targetDirectory}");
        }

        EnsureLinkOrMissing(linkPath);
        var pending = linkPath + PendingSuffix;
        RemoveLink(pending);
        await ProcessRunner.RunAsync(
            CommandShell, $"/c mklink /J \"{pending}\" \"{targetDirectory}\"", cancellationToken: cancellationToken);

        RemoveLink(linkPath);
        Directory.Move(pending, linkPath);
    }

    public string? ReadTarget(string linkPath)
    {
        var info = new DirectoryInfo(linkPath);
        return info.Exists ? info.LinkTarget : null;
    }

    private static void EnsureLinkOrMissing(string linkPath)
    {
        var info = new DirectoryInfo(linkPath);
        if (info.Exists && info.LinkTarget is null)
        {
            throw new InvalidOperationException($"'{linkPath}' is a real directory, not a link; refusing to replace it.");
        }
    }

    private static void RemoveLink(string linkPath)
    {
        EnsureLinkOrMissing(linkPath);
        var info = new DirectoryInfo(linkPath);
        if (info.Exists)
        {
            info.Delete();
        }
    }
}
