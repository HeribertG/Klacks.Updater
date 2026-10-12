// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using NUnit.Framework;
using Shouldly;

/// <summary>
/// Runs pg_dump/psql against a real PostgreSQL. Only when KLACKS_UPDATER_PG_IT holds a connection string to a
/// server whose user may create databases, and KLACKS_UPDATER_PG_BIN (optional) points at the client binaries.
/// The test creates and drops its own throwaway database.
/// </summary>
[TestFixture]
[Category("LocalPostgres")]
public class NativePgDumpBackupServiceIntegrationTests
{
    private const string ConnectionVariable = "KLACKS_UPDATER_PG_IT";
    private const string BinVariable = "KLACKS_UPDATER_PG_BIN";

    private string _adminConnection = string.Empty;
    private string _database = string.Empty;
    private string _backupDirectory = string.Empty;

    [SetUp]
    public async Task SetUp()
    {
        _adminConnection = Environment.GetEnvironmentVariable(ConnectionVariable) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_adminConnection))
        {
            Assert.Ignore($"{ConnectionVariable} not set.");
        }

        _database = "klacks_updater_it_" + Guid.NewGuid().ToString("N")[..8];
        _backupDirectory = Path.Combine(Path.GetTempPath(), _database);
        await ExecuteAsync(_adminConnection, $"CREATE DATABASE {_database}");
        await ExecuteAsync(DatabaseConnection(), "CREATE TABLE client (name text); INSERT INTO client VALUES ('before');");
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
        if (Directory.Exists(_backupDirectory))
        {
            Directory.Delete(_backupDirectory, recursive: true);
        }
    }

    private string DatabaseConnection()
    {
        return new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _database }.ConnectionString;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadNamesAsync()
    {
        await using var connection = new NpgsqlConnection(DatabaseConnection());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT name FROM client ORDER BY name", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Test]
    public async Task Backup_then_restore_brings_back_the_state_and_removes_later_tables()
    {
        var service = new NativePgDumpBackupService(
            Options.Create(new UpdaterOptions
            {
                ConnectionString = DatabaseConnection(),
                BackupDirectory = _backupDirectory,
                PostgresBinDirectory = Environment.GetEnvironmentVariable(BinVariable) ?? string.Empty,
            }),
            NullLogger<NativePgDumpBackupService>.Instance);

        var backup = await service.CreateAsync();
        await ExecuteAsync(DatabaseConnection(), "INSERT INTO client VALUES ('after'); CREATE TABLE migrated (id int);");

        await service.RestoreAsync(backup);

        (await ReadNamesAsync()).ShouldBe(["before"]);
        await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(DatabaseConnection(), "SELECT 1 FROM migrated"));
    }
}
