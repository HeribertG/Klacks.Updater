// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Infrastructure.EngineUpgrade;
using Microsoft.Extensions.Options;
using Npgsql;
using NUnit.Framework;
using Shouldly;

/// <summary>
/// Reads cluster facts and a content snapshot from a real PostgreSQL. Only when KLACKS_UPDATER_PG_IT holds a connection
/// string to a server whose user may create databases. The test creates and drops its own throwaway database.
/// </summary>
[TestFixture]
[Category("LocalPostgres")]
public class NpgsqlEngineSnapshotReaderIntegrationTests
{
    private const string ConnectionVariable = "KLACKS_UPDATER_PG_IT";

    private string _adminConnection = string.Empty;
    private string _database = string.Empty;

    [SetUp]
    public async Task SetUp()
    {
        _adminConnection = Environment.GetEnvironmentVariable(ConnectionVariable) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_adminConnection))
        {
            Assert.Ignore($"{ConnectionVariable} not set.");
        }

        _database = "klacks_engine_it_" + Guid.NewGuid().ToString("N")[..8];
        await ExecuteAsync(_adminConnection, $"CREATE DATABASE {_database}");
        await ExecuteAsync(DatabaseConnection(), """"
            CREATE EXTENSION IF NOT EXISTS pg_trgm;
            CREATE SCHEMA "Odd Schema";
            CREATE TABLE "Odd Schema"."Weird ""Table""" (id int);
            INSERT INTO "Odd Schema"."Weird ""Table""" SELECT generate_series(1, 7);
            CREATE TABLE "__EFMigrationsHistory" ("MigrationId" text PRIMARY KEY, "ProductVersion" text);
            INSERT INTO "__EFMigrationsHistory" VALUES ('20250101_A', '10'), ('20261119_B', '10');
            """");
    }

    [TearDown]
    public async Task TearDown()
    {
        if (string.IsNullOrWhiteSpace(_database))
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync(_adminConnection, $"DROP DATABASE IF EXISTS {_database} WITH (FORCE)");
    }

    [Test]
    public async Task Facts_and_snapshot_reflect_the_real_cluster()
    {
        var reader = new NpgsqlEngineSnapshotReader(Options.Create(new UpdaterOptions { ConnectionString = _adminConnection }));

        var facts = await reader.ReadClusterFactsAsync(CancellationToken.None);
        var snapshot = await reader.SnapshotAsync(_database, CancellationToken.None);
        await reader.AnalyzeAsync(_database, CancellationToken.None);

        facts.ServerMajor.ShouldBeGreaterThanOrEqualTo(14);
        facts.Databases.ShouldContain(database => database.Name == _database);
        facts.Databases.ShouldNotContain(database => database.Name == NpgsqlEngineSnapshotReader.MaintenanceDatabase);
        facts.TotalDatabaseBytes.ShouldBeGreaterThan(0);
        snapshot.RowCounts["\"Odd Schema\".\"Weird \"\"Table\"\"\""].ShouldBe(7);
        snapshot.RowCounts["public.\"__EFMigrationsHistory\""].ShouldBe(2);
        snapshot.Extensions.ShouldContain("pg_trgm");
        snapshot.MaxMigrationId.ShouldBe("20261119_B");
        snapshot.InvalidIndexes.ShouldBeEmpty();
    }

    private string DatabaseConnection() =>
        new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _database }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
