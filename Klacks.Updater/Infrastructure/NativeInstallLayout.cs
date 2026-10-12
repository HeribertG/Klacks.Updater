// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Directory layout of a native installation below the program root: one immutable directory per version under
/// "versions", a "current" link the service runs from, and a staging area for downloads that are not verified yet.
/// Version names come from the update row and are validated, so they can never escape the versions directory.
/// </summary>
/// <param name="programRoot">Absolute program root of the native installation</param>

using System.Text.RegularExpressions;

namespace Klacks.Updater.Infrastructure;

public partial class NativeInstallLayout
{
    private const string VersionsDirectoryName = "versions";
    private const string CurrentLinkName = "current";
    private const string StagingDirectoryName = "staging";

    public NativeInstallLayout(string programRoot)
    {
        if (string.IsNullOrWhiteSpace(programRoot) || !Path.IsPathRooted(programRoot))
        {
            throw new InvalidOperationException("Updater:NativeProgramRoot must be an absolute path in native mode.");
        }

        ProgramRoot = Path.GetFullPath(programRoot);
    }

    public string ProgramRoot { get; }

    public string VersionsDirectory => Path.Combine(ProgramRoot, VersionsDirectoryName);

    public string CurrentLink => Path.Combine(ProgramRoot, CurrentLinkName);

    public string StagingDirectory => Path.Combine(ProgramRoot, StagingDirectoryName);

    public string VersionDirectory(string version)
    {
        if (!VersionPattern().IsMatch(version))
        {
            throw new InvalidOperationException($"'{version}' is not a valid version name.");
        }

        return Path.Combine(VersionsDirectory, version);
    }

    [GeneratedRegex(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$")]
    private static partial Regex VersionPattern();
}
