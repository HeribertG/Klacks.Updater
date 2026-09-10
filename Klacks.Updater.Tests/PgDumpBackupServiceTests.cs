// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Tests;

using Klacks.Updater.Infrastructure;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class PgDumpBackupServiceTests
{
    [Test]
    public void IsRestorableDump_accepts_a_pg_dump_with_tables()
    {
        const string dump = "--\n-- PostgreSQL database dump\n--\n\nCREATE TABLE public.client (\n    id uuid NOT NULL\n);\n";

        PgDumpBackupService.IsRestorableDump(dump).ShouldBeTrue();
    }

    [Test]
    public void IsRestorableDump_rejects_an_empty_file()
    {
        PgDumpBackupService.IsRestorableDump(string.Empty).ShouldBeFalse();
    }

    [Test]
    public void IsRestorableDump_rejects_a_file_without_the_dump_header()
    {
        PgDumpBackupService.IsRestorableDump("CREATE TABLE public.client (id uuid);").ShouldBeFalse();
    }

    [Test]
    public void IsRestorableDump_rejects_a_header_only_dump()
    {
        PgDumpBackupService.IsRestorableDump("--\n-- PostgreSQL database dump\n--\n").ShouldBeFalse();
    }

    [Test]
    public void RecreateDatabaseScript_drops_with_force_and_recreates_with_the_owner()
    {
        var script = PgDumpBackupService.RecreateDatabaseScript("Klacks", "admin");

        script.ShouldContain("DROP DATABASE IF EXISTS \"Klacks\" WITH (FORCE);");
        script.ShouldContain("CREATE DATABASE \"Klacks\" OWNER \"admin\";");
        script.IndexOf("DROP DATABASE", StringComparison.Ordinal)
            .ShouldBeLessThan(script.IndexOf("CREATE DATABASE", StringComparison.Ordinal));
    }

    [Test]
    public void RecreateDatabaseScript_escapes_quotes_in_identifiers()
    {
        var script = PgDumpBackupService.RecreateDatabaseScript("odd\"name", "adm\"in");

        script.ShouldContain("\"odd\"\"name\"");
        script.ShouldContain("\"adm\"\"in\"");
    }
}
