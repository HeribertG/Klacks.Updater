// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Domain.EngineUpgrade;
using Klacks.Updater.Infrastructure.EngineUpgrade.Native;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class NativeEngineCommandsTests
{
    private const string PgCtl = @"C:\Program Files\Klacks Über\pgsql-19\bin\pg_ctl.exe";
    private const string Data = @"D:\Daten Ä\Klacks\pgdata-19";

    [Test]
    public void Image_path_quotes_both_full_paths_like_pg_ctl_register()
    {
        NativeEngineCommands.BuildServiceImagePath(PgCtl, "KlacksPostgres", Data)
            .ShouldBe("\"C:\\Program Files\\Klacks Über\\pgsql-19\\bin\\pg_ctl.exe\" runservice -N \"KlacksPostgres\" -D \"D:\\Daten Ä\\Klacks\\pgdata-19\" -w");
    }

    [Test]
    public void Image_path_parses_back_into_pg_ctl_and_data_directory()
    {
        var parsed = NativeEngineCommands.ParseServiceImagePath(NativeEngineCommands.BuildServiceImagePath(PgCtl, "KlacksPostgres", Data));

        parsed.ShouldBe((PgCtl, Data));
    }

    [Test]
    public void Image_path_written_by_pg_ctl_register_with_more_options_is_understood()
    {
        var parsed = NativeEngineCommands.ParseServiceImagePath(
            "\"C:\\Klacks\\pgsql\\bin\\pg_ctl.exe\" runservice -N \"KlacksPostgres\" -D \"C:\\ProgramData\\Klacks\\pgdata\" -w -t 60");

        parsed.ShouldBe((@"C:\Klacks\pgsql\bin\pg_ctl.exe", @"C:\ProgramData\Klacks\pgdata"));
    }

    [TestCase("")]
    [TestCase("\"C:\\Klacks\\Klacks.Api.exe\"")]
    [TestCase("C:\\pgsql\\bin\\pg_ctl.exe runservice -N KlacksPostgres -D C:\\pgdata")]
    public void Foreign_image_path_is_not_parsed(string imagePath) =>
        NativeEngineCommands.ParseServiceImagePath(imagePath).ShouldBeNull();

    [Test]
    public void Sc_arguments_keep_the_image_path_as_one_argument()
    {
        var imagePath = NativeEngineCommands.BuildServiceImagePath(PgCtl, "KlacksPostgres", Data);

        NativeEngineCommands.BuildSetBinPathArguments("KlacksPostgres", imagePath).ShouldBe(["config", "KlacksPostgres", "binPath=", imagePath]);
    }

    [Test]
    public void Updater_dependencies_lose_only_the_postgres_service()
    {
        var remaining = NativeEngineCommands.DependenciesWithout(["klackspostgres", "Tcpip"], "KlacksPostgres");

        remaining.ShouldBe(["Tcpip"]);
        NativeEngineCommands.BuildSetDependenciesArguments("KlacksUpdater", remaining).ShouldBe(["config", "KlacksUpdater", "depend=", "Tcpip"]);
        NativeEngineCommands.BuildSetDependenciesArguments("KlacksUpdater", []).ShouldBe(["config", "KlacksUpdater", "depend=", "/"]);
    }

    [Test]
    public void Initdb_arguments_use_scram_and_a_password_file()
    {
        NativeEngineCommands.BuildInitDbArguments(Data, "klacks", @"C:\backups\engine-upgrade\x.pw").ShouldBe(
        [
            "-D", Data, "-U", "klacks", "-E", "UTF8", "--auth-local=scram-sha-256", "--auth-host=scram-sha-256",
            @"--pwfile=C:\backups\engine-upgrade\x.pw", "--no-instructions",
        ]);
    }

    [Test]
    public void Dump_and_restore_never_take_the_database_name_as_a_plain_argument()
    {
        NativeEngineCommands.BuildPgDumpArguments(@"C:\b\x.pgdump").ShouldBe(["--format=custom", @"--file=C:\b\x.pgdump", "--no-password"]);
        NativeEngineCommands.BuildPgRestoreArguments("-d evil", @"C:\b\x.pgdump")
            .ShouldBe(["--exit-on-error", "--no-password", "--dbname=dbname='-d evil'", @"C:\b\x.pgdump"]);
        NativeEngineCommands.ConnectionInfoForDatabase("it's\\x").ShouldBe("dbname='it\\'s\\\\x'");
    }

    [Test]
    public void Dump_file_names_do_not_contain_database_names()
    {
        var operationId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        NativeEngineCommands.DumpFileName(operationId, 2).ShouldBe("11111111222233334444555555555555-2.pgdump");
    }

    [Test]
    public void Create_database_preserves_encoding_and_locale_and_quotes_everything()
    {
        var sql = NativeEngineCommands.BuildCreateDatabaseSql(
            new EngineDatabase("Kl\"acks Ü", "kla'cks", "UTF8", "German_Switzerland.1252", "German_Switzerland.1252", "libc", null));

        sql.ShouldBe("CREATE DATABASE \"Kl\"\"acks Ü\" WITH OWNER \"kla'cks\" TEMPLATE template0 ENCODING 'UTF8' LC_COLLATE 'German_Switzerland.1252' LC_CTYPE 'German_Switzerland.1252' LOCALE_PROVIDER libc");
    }

    [Test]
    public void Create_database_carries_an_icu_locale()
    {
        NativeEngineCommands.BuildCreateDatabaseSql(new EngineDatabase("x", "klacks", "UTF8", "C", "C", "icu", "de-CH"))
            .ShouldEndWith("LOCALE_PROVIDER icu LOCALE 'de-CH'");
        Should.Throw<InvalidOperationException>(() =>
            NativeEngineCommands.BuildCreateDatabaseSql(new EngineDatabase("x", "klacks", "UTF8", "C", "C", "libc; DROP", null)));
    }

    [Test]
    public void Klacks_block_is_taken_from_the_old_configuration()
    {
        string[] old = ["max_connections = 100", NativeEngineCommands.KlacksConfigurationMarker, "port = 5439", "listen_addresses = 'localhost'", "timezone = 'Europe/Zurich'"];

        NativeEngineCommands.KlacksConfigurationBlock(old, 1).ShouldBe(["", NativeEngineCommands.KlacksConfigurationMarker, "port = 5439", "listen_addresses = 'localhost'", "timezone = 'Europe/Zurich'"]);
        NativeEngineCommands.KlacksConfigurationBlock(["max_connections = 100"], 5440)
            .ShouldBe(["", NativeEngineCommands.KlacksConfigurationMarker, "port = 5440", "listen_addresses = 'localhost'"]);
    }

    [Test]
    public void Journaled_paths_must_stay_below_the_installation_roots()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows paths are only meaningful on Windows.");
        }

        NativeEngineCommands.IsBelow(@"C:\Program Files\Klacks\pgsql\bin\pg_ctl.exe", @"C:\Program Files\Klacks").ShouldBeTrue();
        NativeEngineCommands.IsBelow(@"c:/program files/klacks/pgsql-19/bin", @"C:\Program Files\Klacks\").ShouldBeTrue();
        NativeEngineCommands.IsBelow(@"C:\Program Files\Klacks Evil\pg_ctl.exe", @"C:\Program Files\Klacks").ShouldBeFalse();
        NativeEngineCommands.IsBelow(@"C:\Program Files\Klacks\..\Evil\pg_ctl.exe", @"C:\Program Files\Klacks").ShouldBeFalse();
        NativeEngineCommands.IsBelow(@"\\server\share\pg_ctl.exe", @"C:\Program Files\Klacks").ShouldBeFalse();
        NativeEngineCommands.IsSamePath(@"C:\ProgramData\Klacks\pgdata-19\", @"c:\programdata\klacks\pgdata-19").ShouldBeTrue();
    }

    [Test]
    public void Layout_pins_versioned_directories_and_the_marker()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Windows paths are only rooted on Windows.");
        }

        var layout = new NativeEngineLayout(@"C:\Program Files\Klacks", @"C:\ProgramData\Klacks");

        layout.EngineDirectory(19).ShouldBe(Path.Combine(@"C:\Program Files\Klacks", "pgsql-19"));
        layout.DataDirectory(19).ShouldBe(Path.Combine(@"C:\ProgramData\Klacks", "pgdata-19"));
        layout.PointerPath.ShouldBe(Path.Combine(@"C:\ProgramData\Klacks", "config", "postgres-engine.json"));
        NativeEngineLayout.CreatedByMarker(layout.DataDirectory(19)).ShouldBe(layout.DataDirectory(19) + ".klacks-engine-upgrade");
    }
}
