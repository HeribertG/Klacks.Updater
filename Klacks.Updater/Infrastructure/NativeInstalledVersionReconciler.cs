// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Native reconciler: reads the version directory "current" points at (the same link the bundle applier switches)
/// and, when it is a version directory directly below the layout's versions directory, records its name through the installed-version recorder, which only updates an existing setup entry and never
/// creates one. A missing link, a target outside the versions directory or not named like a version, and any recorder
/// error are only logged.
/// </summary>
/// <param name="options">Updater configuration (program root of the native installation)</param>
/// <param name="linkSwitcher">Reads the target of the "current" link</param>
/// <param name="installedVersionRecorder">Writes the version into the operating system's installation entry</param>
/// <param name="logger">Logger for diagnostic output</param>

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class NativeInstalledVersionReconciler : IInstalledVersionReconciler
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly UpdaterOptions _options;
    private readonly IVersionLinkSwitcher _linkSwitcher;
    private readonly IInstalledVersionRecorder _installedVersionRecorder;
    private readonly ILogger<NativeInstalledVersionReconciler> _logger;

    public NativeInstalledVersionReconciler(
        IOptions<UpdaterOptions> options,
        IVersionLinkSwitcher linkSwitcher,
        IInstalledVersionRecorder installedVersionRecorder,
        ILogger<NativeInstalledVersionReconciler> logger)
    {
        _options = options.Value;
        _linkSwitcher = linkSwitcher;
        _installedVersionRecorder = installedVersionRecorder;
        _logger = logger;
    }

    public void Reconcile()
    {
        try
        {
            var layout = new NativeInstallLayout(_options.NativeProgramRoot);
            var target = _linkSwitcher.ReadTarget(layout.CurrentLink);
            if (string.IsNullOrWhiteSpace(target))
            {
                _logger.LogWarning("{Link} points nowhere; installed version not reconciled.", layout.CurrentLink);
                return;
            }

            var targetDirectory = Path.GetFullPath(target, layout.ProgramRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var version = Path.GetFileName(targetDirectory);
            if (!IsBelowVersionsDirectory(layout, targetDirectory) || !NativeInstallLayout.IsVersionName(version))
            {
                _logger.LogWarning("{Link} points at {Target}, which is not a version directory; installed version not reconciled.",
                    layout.CurrentLink, target);
                return;
            }

            _installedVersionRecorder.Record(version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not reconcile the installed version with the setup's uninstall entry.");
        }
    }

    private static bool IsBelowVersionsDirectory(NativeInstallLayout layout, string targetDirectory)
    {
        var parent = Path.GetDirectoryName(targetDirectory);
        var versionsDirectory = Path.GetFullPath(layout.VersionsDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parent is not null && string.Equals(parent, versionsDirectory, PathComparison);
    }
}
