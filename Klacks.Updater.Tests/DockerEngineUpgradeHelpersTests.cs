// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Domain.EngineUpgrade;
using Klacks.Updater.Infrastructure.EngineUpgrade.Docker;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class DockerEngineUpgradeHelpersTests
{
    private const string Key = "KLACKS_POSTGRES_IMAGE";

    [Test]
    public void EnvFile_ReadRaw_AbsentKey_IsMissing() =>
        DockerEngineEnvFile.ReadRaw(["A=1"], Key).ShouldBe(DockerEngineStateKeys.Missing);

    [Test]
    public void EnvFile_ReadRaw_KeepsRawValueIncludingQuotesAndEmpty()
    {
        DockerEngineEnvFile.ReadRaw([Key + "=\"x:1\""], Key).ShouldBe("\"x:1\"");
        DockerEngineEnvFile.ReadRaw([Key + "="], Key).ShouldBe(string.Empty);
    }

    [Test]
    public void EnvFile_Apply_ReplacesInPlaceAndKeepsOtherLines() =>
        DockerEngineEnvFile.Apply(["A=1", Key + "=old", "B=2"], Key, "new").ShouldBe(["A=1", Key + "=new", "B=2"]);

    [Test]
    public void EnvFile_Apply_AppendsAbsentKey() =>
        DockerEngineEnvFile.Apply(["A=1"], Key, "new").ShouldBe(["A=1", Key + "=new"]);

    [Test]
    public void EnvFile_Apply_MissingRemovesEveryOccurrence() =>
        DockerEngineEnvFile.Apply([Key + "=a", "A=1", Key + "=b"], Key, DockerEngineStateKeys.Missing).ShouldBe(["A=1"]);

    [Test]
    public void EnvFile_Apply_CollapsesDuplicates() =>
        DockerEngineEnvFile.Apply([Key + "=a", "A=1", Key + "=b"], Key, "c").ShouldBe([Key + "=c", "A=1"]);

    [Test]
    public void EnvFile_Apply_DoesNotMatchLongerKey() =>
        DockerEngineEnvFile.Apply([Key + "_X=1"], Key, "c").ShouldBe([Key + "_X=1", Key + "=c"]);

    [TestCase("a\nB=1")]
    [TestCase("a\rB=1")]
    public void EnvFile_Apply_RefusesLineBreaks(string value) =>
        Should.Throw<InvalidOperationException>(() => DockerEngineEnvFile.Apply([], Key, value));

    [Test]
    public void EnvFile_ApplyRawRoundTrip_RestoresOriginal()
    {
        string[] original = ["A=1", Key + "=  'quoted' ", "B=2"];
        var raw = DockerEngineEnvFile.ReadRaw(original, Key);
        var changed = DockerEngineEnvFile.Apply(original, Key, "new");

        DockerEngineEnvFile.Apply(changed, Key, raw).ShouldBe(original);
    }

    [TestCase("ghcr.io/heribertg/klacks-postgres:19", "ghcr.io/heribertg/klacks-postgres")]
    [TestCase("ghcr.io/heribertg/klacks-postgres", "ghcr.io/heribertg/klacks-postgres")]
    [TestCase("ghcr.io/heribertg/klacks-postgres@sha256:abc", "ghcr.io/heribertg/klacks-postgres")]
    [TestCase("ghcr.io/heribertg/klacks-postgres:19@sha256:abc", "ghcr.io/heribertg/klacks-postgres")]
    [TestCase("localhost:5000/klacks-postgres:19", "localhost:5000/klacks-postgres")]
    [TestCase("localhost:5000/klacks-postgres", "localhost:5000/klacks-postgres")]
    public void ImageReference_Repository_StripsTagAndDigest(string reference, string expected) =>
        PostgresImageReference.Repository(reference).ShouldBe(expected);

    [Test]
    public void ImageReference_IsPinnedFor_RequiresRepositoryAndFullDigest()
    {
        const string repository = "ghcr.io/heribertg/klacks-postgres";
        var digest = "sha256:" + new string('a', 64);

        PostgresImageReference.IsPinnedFor(repository + "@" + digest, repository).ShouldBeTrue();
        PostgresImageReference.IsPinnedFor(repository + ":19", repository).ShouldBeFalse();
        PostgresImageReference.IsPinnedFor(repository + "x@" + digest, repository).ShouldBeFalse();
        PostgresImageReference.IsPinnedFor(repository + "@" + digest + "\nA=1", repository).ShouldBeFalse();
    }

    [TestCase("/var/lib/postgresql/data/pgdata", "pgdata")]
    [TestCase("/var/lib/postgresql/data/pg19", "pg19")]
    [TestCase("/var/lib/postgresql/data", null)]
    [TestCase("/var/lib/postgresql/data/", null)]
    [TestCase("/var/lib/postgresql/data/..", null)]
    [TestCase("/var/lib/postgresql/data/a/b", null)]
    [TestCase("/var/lib/postgresql/19/docker", null)]
    public void Paths_SubdirOf_AcceptsOnlyOneSafeSegment(string dataDirectory, string? expected) =>
        DockerEnginePaths.SubdirOf(dataDirectory).ShouldBe(expected);

    [Test]
    public void Sql_CreateDatabase_QuotesIdentifiersAndLiterals()
    {
        var database = new EngineDatabase("we\"ird", "ad\"min", "UTF8", "de_CH.utf8'", "C", "builtin", "C.UTF-8");

        DockerEngineSql.CreateDatabase(database).ShouldBe(
            "CREATE DATABASE \"we\"\"ird\" WITH OWNER \"ad\"\"min\" TEMPLATE template0 ENCODING 'UTF8' LC_COLLATE 'de_CH.utf8''' LC_CTYPE 'C' LOCALE_PROVIDER builtin LOCALE 'C.UTF-8'");
    }

    [Test]
    public void Sql_CreateDatabase_UnknownProvider_Throws() =>
        Should.Throw<InvalidOperationException>(() =>
            DockerEngineSql.CreateDatabase(new EngineDatabase("a", "b", "UTF8", "C", "C", "x", null)));

    [TestCase("Klacks", "dbname='Klacks'")]
    [TestCase("a'b", "dbname='a\\'b'")]
    [TestCase("a\\b", "dbname='a\\\\b'")]
    [TestCase("postgresql://evil", "dbname='postgresql://evil'")]
    public void Sql_ConnectionDatabase_QuotesAsConninfo(string name, string expected) =>
        DockerEngineSql.ConnectionDatabase(name).ShouldBe(expected);

    [TestCase("missing", true)]
    [TestCase("ghcr.io/heribertg/klacks-postgres@sha256:ab", true)]
    [TestCase("\"pgvector/pgvector:pg17\"", true)]
    [TestCase("", true)]
    [TestCase("${X}", false)]
    [TestCase("a\nB=1", false)]
    [TestCase("pgdata\n", false)]
    public void EnvFile_IsRestorableRawValue(string value, bool expected) =>
        DockerEngineEnvFile.IsRestorableRawValue(value).ShouldBe(expected);

    [Test]
    public void Regexes_DoNotAcceptTrailingNewline()
    {
        PostgresImageReference.IsDigest("sha256:" + new string('a', 64) + "\n").ShouldBeFalse();
        DockerEnginePaths.IsValidSubdir("pgdata\n").ShouldBeFalse();
    }

    [Test]
    public void Paths_OwnershipMarker_DepthMatchesTheExchangeLayout()
    {
        var marker = DockerEnginePaths.OwnershipMarker(Guid.Empty, "pg19");

        marker.ShouldBe("/var/lib/postgresql/data/engine-upgrade/00000000000000000000000000000000/owns-pg19");
        DockerEnginePaths.DepthBelowDataRoot(marker).ShouldBe("3");
    }

    [Test]
    public void Output_ParseIdentity_ReturnsMajorAndDirectory() =>
        DockerEngineCommandOutput.ParseIdentity("190001|/var/lib/postgresql/data/pg19\n").ShouldBe((19, "/var/lib/postgresql/data/pg19"));

    [Test]
    public void Output_ParseAvailableBytes_ReadsAvailableColumnInKilobytes() =>
        DockerEngineCommandOutput.ParseAvailableBytes("Filesystem 1024-blocks Used Available Capacity Mounted on\noverlay 100 40 60 40% /var/lib/postgresql/data\n")
            .ShouldBe(60L * 1024);

    [Test]
    public void ComposeConfig_ReadService_ReturnsNullForMissingService() =>
        ComposeConfigReader.ReadService("{\"services\":{\"other\":{}}}", "postgres").ShouldBeNull();
}
