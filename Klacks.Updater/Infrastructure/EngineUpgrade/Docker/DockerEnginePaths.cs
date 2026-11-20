// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;
using System.Text.RegularExpressions;
using Klacks.Updater.Application;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

/// <summary>
/// Paths inside the postgres containers of the Docker engine upgrade. The postgres volume is mounted at DataRoot in the
/// old and in the new container; each cluster lives in one subdirectory of it (pgdata for the first install,
/// pg&lt;major&gt; after an upgrade) and the dumps go to a per-operation folder of the exchange directory, next to the
/// ownership marker that proves this operation created the target folder. Built with '/'
/// on purpose: these are Linux container paths even when the updater tests run on Windows.
/// </summary>
public static partial class DockerEnginePaths
{
    public const string DataRoot = "/var/lib/postgresql/data";

    private const char Separator = '/';
    private const string TargetSubdirPrefix = "pg";
    private const string VersionFileName = "PG_VERSION";
    private const string OwnershipMarkerPrefix = "owns-";
    private const string DumpFileFormat = "{0:D3}.dump";
    private const string OperationFolderFormat = "N";
    private const string CurrentDirectory = ".";
    private const string ParentDirectory = "..";

    public static string DataDirectory(string subdir) => DataRoot + Separator + subdir;

    public static string VersionFile(string subdir) => DataDirectory(subdir) + Separator + VersionFileName;

    public static string TargetSubdir(int major) => TargetSubdirPrefix + major.ToString(CultureInfo.InvariantCulture);

    public static string DumpDirectory(Guid operationId) =>
        UpdaterConstants.DockerEngineUpgradeExchangeDirectory + Separator + operationId.ToString(OperationFolderFormat, CultureInfo.InvariantCulture);

    public static string DumpFile(Guid operationId, int index) =>
        DumpDirectory(operationId) + Separator + string.Format(CultureInfo.InvariantCulture, DumpFileFormat, index);

    public static string OwnershipMarker(Guid operationId, string targetSubdir) =>
        DumpDirectory(operationId) + Separator + OwnershipMarkerPrefix + targetSubdir;

    public static string DepthBelowDataRoot(string path) =>
        path[DataRoot.Length..].Count(character => character == Separator).ToString(CultureInfo.InvariantCulture);

    public static bool IsValidSubdir(string? subdir) =>
        !string.IsNullOrEmpty(subdir)
        && subdir != CurrentDirectory
        && subdir != ParentDirectory
        && SubdirPattern().IsMatch(subdir);

    public static string? SubdirOf(string? dataDirectory)
    {
        var prefix = DataRoot + Separator;
        if (dataDirectory is null || !dataDirectory.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var subdir = dataDirectory[prefix.Length..];
        return IsValidSubdir(subdir) ? subdir : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex SubdirPattern();
}
