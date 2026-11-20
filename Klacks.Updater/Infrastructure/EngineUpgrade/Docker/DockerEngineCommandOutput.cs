// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Globalization;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

/// <summary>
/// Parsers for the plain-text output of the commands the Docker engine upgrade runs inside the postgres container:
/// PG_VERSION, `df -Pk` (free bytes of the volume), `ls -A` of the volume root and the identity probe (server major and
/// data directory of the cluster psql reached).
/// </summary>
public static class DockerEngineCommandOutput
{
    private const long KilobyteFactor = 1024;
    private const int DfAvailableColumn = 3;
    private const int DfMinimumLines = 2;
    private const int ServerVersionNumDivisor = 10000;
    private static readonly char[] LineBreaks = ['\r', '\n'];
    private static readonly char[] Whitespace = [' ', '\t'];

    public static int ParseMajor(string pgVersionContent) =>
        int.Parse(pgVersionContent.Trim(), NumberStyles.None, CultureInfo.InvariantCulture);

    public static long ParseAvailableBytes(string dfOutput)
    {
        var lines = dfOutput.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < DfMinimumLines)
        {
            throw new FormatException("Unexpected df output: no data line.");
        }

        var columns = lines[^1].Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length <= DfAvailableColumn)
        {
            throw new FormatException("Unexpected df output: too few columns.");
        }

        return long.Parse(columns[DfAvailableColumn], NumberStyles.None, CultureInfo.InvariantCulture) * KilobyteFactor;
    }

    public static IReadOnlyList<string> ParseEntries(string lsOutput) =>
        lsOutput.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static (int Major, string DataDirectory) ParseIdentity(string probeOutput)
    {
        var value = probeOutput.Trim();
        var separator = value.IndexOf(DockerEngineSql.IdentitySeparator);
        if (separator <= 0)
        {
            throw new FormatException("Unexpected identity probe output.");
        }

        var versionNum = int.Parse(value[..separator], NumberStyles.None, CultureInfo.InvariantCulture);
        return (versionNum / ServerVersionNumDivisor, value[(separator + 1)..]);
    }
}
