// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Text;
using Klacks.Updater.Domain.EngineUpgrade;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

/// <summary>
/// SQL the Docker engine upgrade sends to the new cluster through psql -c (one argument, no shell): the identity probe
/// that proves psql reached the new cluster before anything is dropped, and DROP/CREATE DATABASE that recreate every
/// migrated database from template0 with its original owner, encoding, collation, ctype and locale provider.
/// ConnectionDatabase quotes a database name as conninfo (dbname='...') for pg_dump/pg_restore -d, so no name can be
/// read as a connection string or URI.
/// </summary>
public static class DockerEngineSql
{
    public const string IdentityQuery = "SELECT current_setting('server_version_num') || '|' || current_setting('data_directory')";

    public const char IdentitySeparator = '|';

    public const string LibcProvider = "libc";
    public const string IcuProvider = "icu";
    public const string BuiltinProvider = "builtin";

    private const string Quote = "\"";
    private const string DoubledQuote = "\"\"";
    private const string SingleQuote = "'";
    private const string DoubledSingleQuote = "''";
    private const string ConnectionDatabasePrefix = "dbname='";
    private const string Backslash = "\\";
    private const string DoubledBackslash = "\\\\";
    private const string EscapedSingleQuote = "\\'";

    public static string ConnectionDatabase(string name) =>
        ConnectionDatabasePrefix
        + name.Replace(Backslash, DoubledBackslash, StringComparison.Ordinal).Replace(SingleQuote, EscapedSingleQuote, StringComparison.Ordinal)
        + SingleQuote;

    public static string DropDatabase(string name) => "DROP DATABASE IF EXISTS " + QuoteIdentifier(name) + " WITH (FORCE)";

    public static string CreateDatabase(EngineDatabase database)
    {
        var sql = new StringBuilder()
            .Append("CREATE DATABASE ").Append(QuoteIdentifier(database.Name))
            .Append(" WITH OWNER ").Append(QuoteIdentifier(database.Owner))
            .Append(" TEMPLATE template0")
            .Append(" ENCODING ").Append(QuoteLiteral(database.Encoding))
            .Append(" LC_COLLATE ").Append(QuoteLiteral(database.Collate))
            .Append(" LC_CTYPE ").Append(QuoteLiteral(database.CType))
            .Append(" LOCALE_PROVIDER ").Append(ProviderKeyword(database.LocaleProvider));

        if (!string.IsNullOrEmpty(database.Locale) && database.LocaleProvider != LibcProvider)
        {
            sql.Append(" LOCALE ").Append(QuoteLiteral(database.Locale));
        }

        return sql.ToString();
    }

    public static string QuoteIdentifier(string identifier) => Quote + identifier.Replace(Quote, DoubledQuote, StringComparison.Ordinal) + Quote;

    public static string QuoteLiteral(string literal) => SingleQuote + literal.Replace(SingleQuote, DoubledSingleQuote, StringComparison.Ordinal) + SingleQuote;

    private static string ProviderKeyword(string provider) => provider switch
    {
        LibcProvider or IcuProvider or BuiltinProvider => provider,
        _ => throw new InvalidOperationException($"Unknown locale provider '{provider}'."),
    };
}
