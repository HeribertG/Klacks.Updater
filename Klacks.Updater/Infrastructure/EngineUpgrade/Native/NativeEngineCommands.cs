// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Pure builders for every external command and text the native engine upgrade produces, so quoting is unit-tested:
/// the service ImagePath in the exact form pg_ctl register writes, its parsing back into pg_ctl and data directory,
/// sc.exe argument lists, initdb, pg_ctl stop, pg_dump and pg_restore argument lists (pg_dump takes the database name
/// from PGDATABASE, pg_restore from a quoted libpq connection string, so a name can never be read as an option), the CREATE
/// DATABASE statement with preserved encoding and locale, the Klacks block of postgresql.conf and the dependency list
/// of the updater service without the PostgreSQL service.
/// </summary>
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Klacks.Updater.Domain.EngineUpgrade;
using Klacks.Updater.Infrastructure.EngineUpgrade;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Native;

public static partial class NativeEngineCommands
{
    public const string KlacksConfigurationMarker = "# Klacks native installation";
    public const string DumpFileExtension = ".pgdump";
    public const string NoDependencies = "/";
    public const char DependencySeparator = '/';

    private const string ParentSegment = "..";
    private const char AltSeparator = '/';
    private const string Quote = "\"";
    private const string DoubledQuote = "\"\"";
    private const string SingleQuote = "'";
    private const string DoubledSingleQuote = "''";
    private const string Backslash = "\\";
    private const string DoubledBackslash = "\\\\";
    private const string EscapedSingleQuote = "\\'";
    private const string ScramAuthentication = "scram-sha-256";
    private const string Utf8Encoding = "UTF8";
    private const string PortSetting = "port = {0}";
    private const string ListenLocalhost = "listen_addresses = 'localhost'";

    public static bool IsSamePath(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    public static bool IsBelow(string path, string root)
    {
        var normalizedPath = Normalize(path);
        var normalizedRoot = Normalize(root);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !normalizedPath.Contains(ParentSegment, StringComparison.Ordinal);
    }

    public static string BuildServiceImagePath(string pgCtlPath, string serviceName, string dataDirectory) =>
        $"{Quote}{pgCtlPath}{Quote} runservice -N {Quote}{serviceName}{Quote} -D {Quote}{dataDirectory}{Quote} -w";

    public static (string PgCtlPath, string DataDirectory)? ParseServiceImagePath(string imagePath)
    {
        var match = ImagePathPattern().Match(imagePath ?? string.Empty);
        return match.Success ? (match.Groups["pgctl"].Value, match.Groups["data"].Value) : null;
    }

    public static IReadOnlyList<string> BuildSetBinPathArguments(string serviceName, string imagePath) =>
        ["config", serviceName, "binPath=", imagePath];

    public static IReadOnlyList<string> BuildSetDependenciesArguments(string serviceName, IReadOnlyList<string> dependencies) =>
        ["config", serviceName, "depend=", dependencies.Count == 0 ? NoDependencies : string.Join(DependencySeparator, dependencies)];

    public static IReadOnlyList<string> DependenciesWithout(IEnumerable<string> dependencies, string removed) =>
        dependencies.Where(name => !string.Equals(name, removed, StringComparison.OrdinalIgnoreCase)).ToList();

    public static IReadOnlyList<string> BuildInitDbArguments(string dataDirectory, string superUser, string passwordFile) =>
    [
        "-D", dataDirectory,
        "-U", superUser,
        "-E", Utf8Encoding,
        "--auth-local=" + ScramAuthentication,
        "--auth-host=" + ScramAuthentication,
        "--pwfile=" + passwordFile,
        "--no-instructions",
    ];

    public static IReadOnlyList<string> BuildPgCtlStopArguments(string dataDirectory) =>
        ["stop", "-D", dataDirectory, "-m", "fast", "-w"];

    public static IReadOnlyList<string> BuildPgDumpArguments(string dumpFile) =>
        ["--format=custom", "--file=" + dumpFile, "--no-password"];

    public static IReadOnlyList<string> BuildPgRestoreArguments(string database, string dumpFile) =>
        ["--exit-on-error", "--no-password", "--dbname=" + ConnectionInfoForDatabase(database), dumpFile];

    public static string ConnectionInfoForDatabase(string database) =>
        "dbname=" + SingleQuote
        + database.Replace(Backslash, DoubledBackslash, StringComparison.Ordinal).Replace(SingleQuote, EscapedSingleQuote, StringComparison.Ordinal)
        + SingleQuote;

    public static string DumpFileName(Guid operationId, int index) =>
        string.Format(CultureInfo.InvariantCulture, "{0:N}-{1}{2}", operationId, index, DumpFileExtension);

    public static string QuoteIdentifier(string identifier) => Quote + identifier.Replace(Quote, DoubledQuote, StringComparison.Ordinal) + Quote;

    public static string QuoteLiteral(string literal) => SingleQuote + literal.Replace(SingleQuote, DoubledSingleQuote, StringComparison.Ordinal) + SingleQuote;

    public static string BuildCreateDatabaseSql(EngineDatabase database)
    {
        var sql = new StringBuilder()
            .Append("CREATE DATABASE ").Append(QuoteIdentifier(database.Name))
            .Append(" WITH OWNER ").Append(QuoteIdentifier(database.Owner))
            .Append(" TEMPLATE template0")
            .Append(" ENCODING ").Append(QuoteLiteral(database.Encoding))
            .Append(" LC_COLLATE ").Append(QuoteLiteral(database.Collate))
            .Append(" LC_CTYPE ").Append(QuoteLiteral(database.CType))
            .Append(" LOCALE_PROVIDER ").Append(ProviderKeyword(database.LocaleProvider));

        if (!string.IsNullOrEmpty(database.Locale) && database.LocaleProvider != NpgsqlEngineSnapshotReader.LibcProvider)
        {
            sql.Append(" LOCALE ").Append(QuoteLiteral(database.Locale));
        }

        return sql.ToString();
    }

    public static IReadOnlyList<string> KlacksConfigurationBlock(IReadOnlyList<string> oldConfigurationLines, int fallbackPort)
    {
        var start = -1;
        for (var index = 0; index < oldConfigurationLines.Count; index++)
        {
            if (oldConfigurationLines[index].Trim() == KlacksConfigurationMarker)
            {
                start = index;
            }
        }

        if (start >= 0)
        {
            return [string.Empty, .. oldConfigurationLines.Skip(start)];
        }

        return [string.Empty, KlacksConfigurationMarker, string.Format(CultureInfo.InvariantCulture, PortSetting, fallbackPort), ListenLocalhost];
    }

    private static string Normalize(string path) =>
        path.Replace(AltSeparator, Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);

    private static string ProviderKeyword(string provider) => provider switch
    {
        NpgsqlEngineSnapshotReader.LibcProvider or NpgsqlEngineSnapshotReader.IcuProvider or NpgsqlEngineSnapshotReader.BuiltinProvider => provider,
        _ => throw new InvalidOperationException($"Unknown locale provider '{provider}'."),
    };

    [GeneratedRegex(@"^\s*""(?<pgctl>[^""]+)""\s+runservice\s+-N\s+""[^""]+""\s+-D\s+""(?<data>[^""]+)""(\s+-w)?(\s+-t\s+[0-9]+)?\s*\z", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePathPattern();
}
