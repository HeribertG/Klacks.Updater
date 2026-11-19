// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Npgsql implementation of the target-neutral cluster reads of an engine upgrade. It connects with
/// Updater:ConnectionString and only switches the database name. Facts: server major from server_version_num, all
/// connectable non-template databases except "postgres" with owner, encoding and locale (column names differ between
/// PostgreSQL 14, 15-16 and 17+), roles with an oid of a user object (everything except the bootstrap superuser and the
/// predefined pg_* roles) and the summed database size. Snapshot: exact count(*) of every ordinary table outside the
/// system schemas (identifiers quoted by the server with format('%I.%I')), extension names, highest
/// __EFMigrationsHistory id when that table exists, and invalid indexes.
/// </summary>
/// <param name="options">Updater configuration (connection string)</param>
using System.Globalization;
using Klacks.Updater.Application;
using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain.EngineUpgrade;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Klacks.Updater.Infrastructure.EngineUpgrade;

public class NpgsqlEngineSnapshotReader : IEngineSnapshotReader
{
    public const int FirstNormalObjectId = 16384;
    public const int LocaleColumnMajor = 17;
    public const int IcuLocaleColumnMajor = 15;
    public const string MaintenanceDatabase = "postgres";

    private const string ServerVersionSql = "SHOW server_version_num;";
    private const string ForeignRolesSql = "SELECT rolname FROM pg_roles WHERE oid >= @firstNormalOid ORDER BY rolname;";
    private const string DatabaseFilter = "NOT d.datistemplate AND d.datallowconn AND d.datname <> @maintenance";
    private const string DatabaseSizeSql = "SELECT COALESCE(sum(pg_database_size(d.datname)), 0)::bigint FROM pg_database d WHERE " + DatabaseFilter + ";";
    private const string DatabasesSqlPrefix = "SELECT d.datname, pg_get_userbyid(d.datdba), pg_encoding_to_char(d.encoding), d.datcollate, d.datctype, ";
    private const string DatabasesSqlSuffix = " FROM pg_database d WHERE " + DatabaseFilter + " ORDER BY d.datname;";
    private const string LocaleColumns17 = "d.datlocprovider::text, d.datlocale";
    private const string LocaleColumns15 = "d.datlocprovider::text, d.daticulocale";
    private const string LocaleColumnsLegacy = "'c'::text, NULL::text";

    private const string TablesSql = @"
        SELECT format('%I.%I', n.nspname, c.relname)
        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relkind = 'r'
          AND n.nspname NOT IN ('pg_catalog', 'information_schema')
          AND n.nspname NOT LIKE 'pg\_toast%'
          AND n.nspname NOT LIKE 'pg\_temp%'
        ORDER BY 1;";

    private const string ExtensionsSql = "SELECT extname FROM pg_extension ORDER BY extname;";
    private const string MigrationTableSql = "SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL;";
    private const string MaxMigrationSql = "SELECT max(\"MigrationId\") FROM public.\"__EFMigrationsHistory\";";

    private const string InvalidIndexesSql = @"
        SELECT format('%I.%I', n.nspname, c.relname)
        FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE NOT i.indisvalid
        ORDER BY 1;";

    private const string AnalyzeSql = "ANALYZE;";
    private const string CountSqlPattern = "SELECT count(*) FROM {0};";

    private const char ProviderLibc = 'c';
    private const char ProviderIcu = 'i';
    private const char ProviderBuiltin = 'b';
    public const string LibcProvider = "libc";
    public const string IcuProvider = "icu";
    public const string BuiltinProvider = "builtin";

    private readonly string _connectionString;

    public NpgsqlEngineSnapshotReader(IOptions<UpdaterOptions> options)
    {
        _connectionString = options.Value.ConnectionString;
    }

    public static string DatabasesSql(int serverMajor)
    {
        var localeColumns = serverMajor >= LocaleColumnMajor
            ? LocaleColumns17
            : serverMajor >= IcuLocaleColumnMajor ? LocaleColumns15 : LocaleColumnsLegacy;
        return DatabasesSqlPrefix + localeColumns + DatabasesSqlSuffix;
    }

    public static string ProviderName(string providerCode) => providerCode.FirstOrDefault() switch
    {
        ProviderIcu => IcuProvider,
        ProviderBuiltin => BuiltinProvider,
        ProviderLibc => LibcProvider,
        _ => throw new InvalidOperationException($"Unknown locale provider '{providerCode}'."),
    };

    public async Task<EngineClusterFacts> ReadClusterFactsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(null, cancellationToken);

        var serverMajor = await ReadServerMajorAsync(connection, cancellationToken);
        var databases = new List<EngineDatabase>();
        await using (var command = new NpgsqlCommand(DatabasesSql(serverMajor), connection))
        {
            command.Parameters.AddWithValue("maintenance", MaintenanceDatabase);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                databases.Add(new EngineDatabase(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    ProviderName(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }

        var roles = new List<string>();
        await using (var command = new NpgsqlCommand(ForeignRolesSql, connection))
        {
            command.Parameters.AddWithValue("firstNormalOid", FirstNormalObjectId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                roles.Add(reader.GetString(0));
            }
        }

        long totalBytes;
        await using (var command = new NpgsqlCommand(DatabaseSizeSql, connection))
        {
            command.Parameters.AddWithValue("maintenance", MaintenanceDatabase);
            totalBytes = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        return new EngineClusterFacts(serverMajor, databases, roles, totalBytes);
    }

    public async Task<EngineDatabaseSnapshot> SnapshotAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(database, cancellationToken);

        var tables = await ReadStringsAsync(connection, TablesSql, cancellationToken);
        var rowCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            await using var command = new NpgsqlCommand(string.Format(CultureInfo.InvariantCulture, CountSqlPattern, table), connection);
            rowCounts[table] = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }

        var extensions = await ReadStringsAsync(connection, ExtensionsSql, cancellationToken);
        string? maxMigration = null;
        await using (var command = new NpgsqlCommand(MigrationTableSql, connection))
        {
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                await using var maxCommand = new NpgsqlCommand(MaxMigrationSql, connection);
                maxMigration = await maxCommand.ExecuteScalarAsync(cancellationToken) as string;
            }
        }

        var invalidIndexes = await ReadStringsAsync(connection, InvalidIndexesSql, cancellationToken);
        return new EngineDatabaseSnapshot(database, rowCounts, extensions, maxMigration, invalidIndexes);
    }

    public async Task AnalyzeAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(database, cancellationToken);
        await using var command = new NpgsqlCommand(AnalyzeSql, connection) { CommandTimeout = 0 };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public void ClearConnectionPools() => NpgsqlConnection.ClearAllPools();

    private static async Task<int> ReadServerMajorAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ServerVersionSql, connection);
        var value = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return int.Parse(value ?? string.Empty, NumberStyles.None, CultureInfo.InvariantCulture) / EngineUpgradeConstants.ServerVersionNumDivisor;
    }

    private static async Task<List<string>> ReadStringsAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        var values = new List<string>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async Task<NpgsqlConnection> OpenAsync(string? database, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(_connectionString) { CommandTimeout = 0 };
        if (database is not null)
        {
            builder.Database = database;
        }

        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
