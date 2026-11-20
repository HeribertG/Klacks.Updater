// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Klacks.Updater.Application;
using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain.EngineUpgrade;
using Klacks.Updater.Infrastructure;
using Klacks.Updater.Infrastructure.EngineUpgrade;
using Klacks.Updater.Infrastructure.EngineUpgrade.Native;
using Microsoft.Extensions.Options;
using Npgsql;
using NUnit.Framework;
using Shouldly;

/// <summary>
/// Real dump/restore between two throwaway clusters in process mode (no services): the old cluster is created with
/// KLACKS_UPDATER_ENGINE_IT_OLD_BIN, the new one with KLACKS_UPDATER_ENGINE_IT_NEW_BIN (may be the same major for a
/// local run). Seeds two databases including pgvector with an HNSW index and a quoted table, dumps with the new
/// pg_dump, recreates the databases with the preserved locale, restores with pg_restore --exit-on-error exactly as the
/// native target does, and verifies with the real snapshot reader. Skipped without the environment variables.
/// </summary>
[TestFixture]
[Category("LocalPostgres")]
public class EngineDumpRestoreIntegrationTests
{
    private const string OldBinVariable = "KLACKS_UPDATER_ENGINE_IT_OLD_BIN";
    private const string NewBinVariable = "KLACKS_UPDATER_ENGINE_IT_NEW_BIN";
    private const string SuperUser = "klacks";
    private const string Password = "it-Pa55'word";

    private string _root = string.Empty;
    private string _oldBin = string.Empty;
    private string _newBin = string.Empty;
    private int _oldPort;
    private int _newPort;

    [SetUp]
    public async Task SetUp()
    {
        _oldBin = Environment.GetEnvironmentVariable(OldBinVariable) ?? string.Empty;
        _newBin = Environment.GetEnvironmentVariable(NewBinVariable) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_oldBin) || string.IsNullOrWhiteSpace(_newBin))
        {
            Assert.Ignore($"{OldBinVariable} and {NewBinVariable} not set.");
        }

        _root = Path.Combine(Path.GetTempPath(), "klacks-engine-it-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _oldPort = FreePort();
        _newPort = FreePort();
        await StartClusterAsync(_oldBin, "old", _oldPort);
        await StartClusterAsync(_newBin, "new", _newPort);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (string.IsNullOrWhiteSpace(_root))
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        foreach (var (bin, name) in new[] { (_oldBin, "old"), (_newBin, "new") })
        {
            try
            {
                await ProcessRunner.RunAsync(Executable(bin, "pg_ctl"), NativeEngineCommands.BuildPgCtlStopArguments(Path.Combine(_root, name)));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
        }

        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public async Task Dump_restore_and_verify_move_every_database_with_vector_and_hnsw()
    {
        await ExecuteAsync(_oldPort, "postgres", "CREATE DATABASE \"Klacks\"; CREATE DATABASE klacks_diag;");
        await ExecuteAsync(_oldPort, "Klacks", """"
            CREATE EXTENSION vector;
            CREATE EXTENSION pg_trgm;
            CREATE TABLE "Weird ""Items""" (id int PRIMARY KEY, embedding vector(3));
            INSERT INTO "Weird ""Items""" SELECT g, ARRAY[g, g * 2, g * 3]::vector FROM generate_series(1, 500) g;
            CREATE INDEX items_hnsw ON "Weird ""Items""" USING hnsw (embedding vector_cosine_ops);
            CREATE TABLE "__EFMigrationsHistory" ("MigrationId" text PRIMARY KEY, "ProductVersion" text);
            INSERT INTO "__EFMigrationsHistory" VALUES ('20261119_B', '10');
            """");
        await ExecuteAsync(_oldPort, "klacks_diag", "CREATE TABLE probe (x text); INSERT INTO probe VALUES ('a'), ('b');");

        var oldReader = Reader(_oldPort);
        var facts = await oldReader.ReadClusterFactsAsync(CancellationToken.None);
        facts.ForeignRoles.ShouldBeEmpty();
        var before = new List<EngineDatabaseSnapshot>();
        foreach (var database in facts.Databases)
        {
            before.Add(await oldReader.SnapshotAsync(database.Name, CancellationToken.None));
        }

        var dumps = new List<(EngineDatabase Database, string File)>();
        for (var index = 0; index < facts.Databases.Count; index++)
        {
            var file = Path.Combine(_root, NativeEngineCommands.DumpFileName(Guid.NewGuid(), index));
            var environment = Libpq(_oldPort);
            environment["PGDATABASE"] = facts.Databases[index].Name;
            await ProcessRunner.RunAsync(Executable(_newBin, "pg_dump"), NativeEngineCommands.BuildPgDumpArguments(file), environment: environment);
            dumps.Add((facts.Databases[index], file));
        }

        foreach (var (database, file) in dumps)
        {
            await ExecuteAsync(_newPort, "postgres", NativeEngineCommands.BuildCreateDatabaseSql(database));
            await ProcessRunner.RunAsync(Executable(_newBin, "pg_restore"), NativeEngineCommands.BuildPgRestoreArguments(database.Name, file), environment: Libpq(_newPort));
        }

        var newReader = Reader(_newPort);
        var after = new List<EngineDatabaseSnapshot>();
        foreach (var database in facts.Databases)
        {
            after.Add(await newReader.SnapshotAsync(database.Name, CancellationToken.None));
            await newReader.AnalyzeAsync(database.Name, CancellationToken.None);
        }

        facts.Databases.Select(database => database.Name).ShouldBe(["Klacks", "klacks_diag"]);
        EngineSnapshotComparer.Compare(before, after).ShouldBeEmpty();
        after[0].Extensions.ShouldContain("vector");
        after[0].RowCounts["public.\"Weird \"\"Items\"\"\""].ShouldBe(500);
    }

    private NpgsqlEngineSnapshotReader Reader(int port) =>
        new(Options.Create(new UpdaterOptions { ConnectionString = Connection(port, "postgres") }));

    private static string Connection(int port, string database) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1",
            Port = port,
            Username = SuperUser,
            Password = Password,
            Database = database,
        }.ConnectionString;

    private static Dictionary<string, string> Libpq(int port) => new()
    {
        ["PGHOST"] = "127.0.0.1",
        ["PGPORT"] = port.ToString(CultureInfo.InvariantCulture),
        ["PGUSER"] = SuperUser,
        ["PGPASSWORD"] = Password,
    };

    private async Task StartClusterAsync(string bin, string name, int port)
    {
        var dataDirectory = Path.Combine(_root, name);
        var passwordFile = Path.Combine(_root, name + ".pw");
        await File.WriteAllTextAsync(passwordFile, Password);
        await ProcessRunner.RunAsync(Executable(bin, "initdb"), NativeEngineCommands.BuildInitDbArguments(dataDirectory, SuperUser, passwordFile));
        File.Delete(passwordFile);
        await File.AppendAllLinesAsync(
            Path.Combine(dataDirectory, NativeEngineLayout.ConfigurationFileName),
            NativeEngineCommands.KlacksConfigurationBlock([], port));
        var startInfo = new System.Diagnostics.ProcessStartInfo(Executable(bin, "pg_ctl")) { UseShellExecute = false };
        foreach (var argument in new[] { "start", "-D", dataDirectory, "-w", "-l", Path.Combine(_root, name + ".log") })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0);
    }

    private static string Executable(string bin, string name) =>
        Path.Combine(bin, OperatingSystem.IsWindows() ? name + ".exe" : name);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task ExecuteAsync(int port, string database, string sql)
    {
        await using var connection = new NpgsqlConnection(Connection(port, database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
