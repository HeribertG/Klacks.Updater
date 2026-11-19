// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Directory layout of the PostgreSQL engine of a native Windows installation. Legacy installations run from
/// &lt;ProgramRoot&gt;\pgsql with data in &lt;DataRoot&gt;\pgdata; an engine upgrade adds &lt;ProgramRoot&gt;\pgsql-&lt;major&gt; and
/// &lt;DataRoot&gt;\pgdata-&lt;major&gt; and records the active pair in the pointer &lt;DataRoot&gt;\config\postgres-engine.json.
/// A sidecar marker next to a new data directory names the operation that created it, so a revert only ever deletes a
/// directory it created itself.
/// </summary>
/// <param name="programRoot">Absolute program root of the native installation</param>
/// <param name="dataRoot">Absolute data root of the native installation</param>
using System.Globalization;
using Klacks.Updater.Application;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Native;

public class NativeEngineLayout
{
    public const string LegacyEngineDirectoryName = "pgsql";
    public const string LegacyDataDirectoryName = "pgdata";
    public const string PointerFileName = "postgres-engine.json";
    public const string BinDirectoryName = "bin";
    public const string LibDirectoryName = "lib";
    public const string PostgresExecutableName = "postgres.exe";
    public const string PgCtlExecutableName = "pg_ctl.exe";
    public const string InitDbExecutableName = "initdb.exe";
    public const string PgDumpExecutableName = "pg_dump.exe";
    public const string PgRestoreExecutableName = "pg_restore.exe";
    public const string VectorLibraryName = "vector.dll";
    public const string VersionFileName = "PG_VERSION";
    public const string ConfigurationFileName = "postgresql.conf";
    public const string CreatedByMarkerSuffix = ".klacks-engine-upgrade";

    private const string StagingDirectoryName = "staging";
    private const string MajorSeparator = "-";

    public NativeEngineLayout(string programRoot, string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(programRoot) || !Path.IsPathRooted(programRoot)
            || string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathRooted(dataRoot))
        {
            throw new InvalidOperationException("Updater:NativeProgramRoot and Hosting:DataRoot must be absolute paths in native mode.");
        }

        ProgramRoot = Path.GetFullPath(programRoot);
        DataRoot = Path.GetFullPath(dataRoot);
    }

    public string ProgramRoot { get; }

    public string DataRoot { get; }

    public string ConfigDirectory => Path.Combine(DataRoot, UpdaterConstants.NativeConfigDirectoryName);

    public string PointerPath => Path.Combine(ConfigDirectory, PointerFileName);

    public string NativeConfigPath => Path.Combine(ConfigDirectory, UpdaterConstants.NativeConfigFileName);

    public string LegacyEngineDirectory => Path.Combine(ProgramRoot, LegacyEngineDirectoryName);

    public string LegacyDataDirectory => Path.Combine(DataRoot, LegacyDataDirectoryName);

    public string StagingDirectory => Path.Combine(ProgramRoot, StagingDirectoryName);

    public string EngineDirectory(int major) => Path.Combine(ProgramRoot, LegacyEngineDirectoryName + MajorSeparator + Format(major));

    public string DataDirectory(int major) => Path.Combine(DataRoot, LegacyDataDirectoryName + MajorSeparator + Format(major));

    public static string BinDirectory(string engineDirectory) => Path.Combine(engineDirectory, BinDirectoryName);

    public static string CreatedByMarker(string dataDirectory) => dataDirectory.TrimEnd(Path.DirectorySeparatorChar) + CreatedByMarkerSuffix;

    private static string Format(int major) => major.ToString(CultureInfo.InvariantCulture);
}
