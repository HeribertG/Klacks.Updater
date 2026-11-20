// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Domain.EngineUpgrade;
using Klacks.Updater.Infrastructure.EngineUpgrade.Docker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class DockerEngineUpgradeTargetTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string PinnedImage = "ghcr.io/heribertg/klacks-postgres@" + Digest;
    private const string ArtifactRef = "ghcr.io/heribertg/klacks-postgres:19";
    private const string Config = "compose config --format json postgres";
    private const string StopApi = "compose stop klacks-api";
    private const string StartApi = "compose start klacks-api";
    private const string RestartProxy = "compose restart nginx-proxy";
    private const string UpPostgres = "compose up -d --wait --wait-timeout 600 postgres";
    private const string PrintDataDirectory = "exec klacks-postgres printenv PGDATA";
    private const string Psql = "exec klacks-postgres psql -X -v ON_ERROR_STOP=1 -U admin -d postgres -tA -c ";

    private static readonly Guid OperationId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly string DumpDirectory = "/var/lib/postgresql/data/engine-upgrade/11111111222233334444555555555555";

    private static readonly EngineDatabase[] Databases =
    [
        new("Klacks", "admin", "UTF8", "en_US.utf8", "en_US.utf8", "libc", null),
        new("klacks_diag", "admin", "UTF8", "C", "C", "icu", "und"),
    ];

    private static readonly string[] PinnedSeventeenEnv =
        ["POSTGRES_PASSWORD=secret", "KLACKS_POSTGRES_IMAGE=pgvector/pgvector:pg17", "KLACKS_POSTGRES_DATA_SUBDIR=pgdata", "KLACKS_API_TAG=1.0.78"];

    private static readonly string[] MissingKeysEnv = ["POSTGRES_PASSWORD=secret", "KLACKS_API_TAG=1.0.78"];

    private static readonly string Marker = DumpDirectory + "/owns-pg19";

    private static readonly string[] RevertCalls =
    [
        StopApi,
        UpPostgres,
        PrintDataDirectory,
        "exec klacks-postgres cat /var/lib/postgresql/data/pgdata/PG_VERSION",
        "exec klacks-postgres find /var/lib/postgresql/data -maxdepth 3 -path " + Marker,
        "exec klacks-postgres rm -rf -- /var/lib/postgresql/data/pg19",
        "exec klacks-postgres rm -rf -- " + DumpDirectory,
    ];

    private static readonly string[] RevertCallsWithoutOwnedTarget =
    [
        .. RevertCalls[..^2],
        "exec klacks-postgres rm -rf -- " + DumpDirectory,
    ];

    private static string[] ExpectedRevertCalls(int completedSteps) =>
        completedSteps >= StepsUntilStopOld ? RevertCalls : RevertCallsWithoutOwnedTarget;

    private const int InspectCallCount = 5;
    private const int AllSteps = 8;
    private const int StepsUntilDump = 3;
    private const int StepsUntilStopOld = 4;
    private const int StepsUntilActivate = 5;
    private const int StepsUntilRestore = 6;

    private string _dir = null!;
    private UpdaterOptions _options = null!;
    private FakeDockerEngine _docker = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "klacks-updater-engine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _options = new UpdaterOptions { ComposeProjectDir = _dir };
        _docker = new FakeDockerEngine(EnvPath);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_dir, recursive: true);
    }

    private string EnvPath => Path.Combine(_dir, ".env");

    private string[] ReadEnv() => File.ReadAllLines(EnvPath);

    private DockerEngineUpgradeTarget CreateTarget() =>
        new(Options.Create(_options), _docker.Runner, NullLogger<DockerEngineUpgradeTarget>.Instance, TimeSpan.Zero);

    private int ReadinessProbeCount() =>
        _docker.Runner.Calls.Count(call => call.StartsWith("exec klacks-postgres pg_isready ", StringComparison.Ordinal));

    private static EngineUpgradeContext ContextFor(EngineInspection inspection) =>
        new(OperationId, 17, 19, ArtifactRef, Digest, new Dictionary<string, string>(inspection.OldState), new Dictionary<string, string>());

    private static EngineUpgradeContext JournalOnly(EngineUpgradeContext context) =>
        new(context.OperationId, context.FromMajor, context.ToMajor, string.Empty, string.Empty,
            new Dictionary<string, string>(context.OldState), new Dictionary<string, string>(context.NewState));

    private async Task<EngineUpgradeContext> RunForwardAsync(DockerEngineUpgradeTarget target, int lastStep)
    {
        var inspection = await target.InspectAsync(CancellationToken.None);
        inspection.UnsupportedReason.ShouldBeNull();
        var context = ContextFor(inspection);
        IReadOnlyList<EngineDump> dumps = [];
        Func<Task>[] steps =
        [
            () => target.PrepareArtifactAsync(context, CancellationToken.None),
            () => target.StopApplicationAsync(CancellationToken.None),
            async () => dumps = await target.DumpAsync(context, Databases, CancellationToken.None),
            () => target.StopOldEngineAsync(context, CancellationToken.None),
            () => target.ActivateNewEngineAsync(context, CancellationToken.None),
            () => target.RestoreAsync(context, dumps, CancellationToken.None),
            () => target.CommitConfigurationAsync(context, CancellationToken.None),
            () => target.StartApplicationAsync(CancellationToken.None),
        ];

        for (var step = 0; step < lastStep; step++)
        {
            await steps[step]();
        }

        return context;
    }

    [Test]
    public async Task Inspect_ParametrisedCompose_RecordsEnvValuesEffectiveStateAndFreeSpace()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);

        var inspection = await CreateTarget().InspectAsync(CancellationToken.None);

        inspection.UnsupportedReason.ShouldBeNull();
        inspection.CurrentMajor.ShouldBe(17);
        inspection.FreeBytes.ShouldBe(50000000L * 1024);
        inspection.OldState[DockerEngineStateKeys.EnvImage].ShouldBe("pgvector/pgvector:pg17");
        inspection.OldState[DockerEngineStateKeys.EnvDataSubdir].ShouldBe("pgdata");
        inspection.OldState[DockerEngineStateKeys.Image].ShouldBe("pgvector/pgvector:pg17");
        inspection.OldState[DockerEngineStateKeys.DataSubdir].ShouldBe("pgdata");
        _docker.Runner.Calls.ShouldBe(
        [
            Config,
            Config,
            PrintDataDirectory,
            "exec klacks-postgres cat /var/lib/postgresql/data/pgdata/PG_VERSION",
            "exec klacks-postgres df -Pk /var/lib/postgresql/data",
        ]);
        _docker.Runner.Environments[0].ShouldBeEmpty();
        _docker.Runner.Environments[1][FakeDockerEngine.ImageVar].ShouldBe(DockerEngineUpgradeTarget.ProbeImage);
        _docker.Runner.Environments[1][FakeDockerEngine.SubdirVar].ShouldBe(DockerEngineUpgradeTarget.ProbeSubdir);
    }

    [Test]
    public async Task Inspect_KeysMissingInEnv_RecordsMissing()
    {
        File.WriteAllLines(EnvPath, MissingKeysEnv);

        var inspection = await CreateTarget().InspectAsync(CancellationToken.None);

        inspection.UnsupportedReason.ShouldBeNull();
        inspection.OldState[DockerEngineStateKeys.EnvImage].ShouldBe(DockerEngineStateKeys.Missing);
        inspection.OldState[DockerEngineStateKeys.EnvDataSubdir].ShouldBe(DockerEngineStateKeys.Missing);
        inspection.OldState[DockerEngineStateKeys.Image].ShouldBe(FakeDockerEngine.DefaultImage);
    }

    [Test]
    public async Task Inspect_ComposeWithoutVariables_IsUnsupportedWithoutTouchingTheContainer()
    {
        File.WriteAllLines(EnvPath, MissingKeysEnv);
        _docker.Parametrised = false;

        var inspection = await CreateTarget().InspectAsync(CancellationToken.None);

        inspection.UnsupportedReason.ShouldNotBeNull();
        inspection.UnsupportedReason.ShouldContain("KLACKS_POSTGRES_IMAGE");
        _docker.Runner.Calls.ShouldBe([Config, Config]);
    }

    [Test]
    public async Task Inspect_DuplicateKeyInEnv_IsUnsupported()
    {
        File.WriteAllLines(EnvPath, [.. PinnedSeventeenEnv, "KLACKS_POSTGRES_DATA_SUBDIR=pg19"]);

        var inspection = await CreateTarget().InspectAsync(CancellationToken.None);

        inspection.UnsupportedReason.ShouldNotBeNull();
    }

    [Test]
    public async Task Inspect_OtherContainerName_IsUnsupported()
    {
        _docker.ContainerName = "other-postgres";

        var inspection = await CreateTarget().InspectAsync(CancellationToken.None);

        inspection.UnsupportedReason.ShouldNotBeNull();
    }

    [Test]
    public async Task Inspect_RunningContainerOnOtherDataDirectory_IsUnsupported()
    {
        File.WriteAllLines(EnvPath, MissingKeysEnv);
        _docker.RunningDataDirectory = "/var/lib/postgresql/data/pg19";

        var inspection = await CreateTarget().InspectAsync(CancellationToken.None);

        inspection.UnsupportedReason.ShouldNotBeNull();
    }

    [Test]
    public async Task Inspect_NeverReturnsTheResolvedPassword()
    {
        File.WriteAllLines(EnvPath, MissingKeysEnv);

        var inspection = await CreateTarget().InspectAsync(CancellationToken.None);

        inspection.OldState.Values.ShouldAllBe(value => !value.Contains("secret"));
    }

    [Test]
    public async Task Upgrade_Success_RunsExactCommandSequenceAndPinsEnv()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        var target = CreateTarget();

        var context = await RunForwardAsync(target, AllSteps);

        _docker.Runner.Calls.Skip(InspectCallCount).ShouldBe(
        [
            "pull " + PinnedImage,
            StopApi,
            "exec klacks-postgres install -d -m 0700 /var/lib/postgresql/data/engine-upgrade " + DumpDirectory,
            "exec klacks-postgres chmod 0700 /var/lib/postgresql/data/engine-upgrade " + DumpDirectory,
            "exec klacks-postgres pg_dump -Fc -U admin -d dbname='Klacks' -f " + DumpDirectory + "/000.dump",
            "exec klacks-postgres pg_dump -Fc -U admin -d dbname='klacks_diag' -f " + DumpDirectory + "/001.dump",
            StopApi,
            "exec klacks-postgres ls -A /var/lib/postgresql/data",
            "exec klacks-postgres touch " + Marker,
            "stop -t 120 klacks-postgres",
            UpPostgres,
            "exec klacks-postgres pg_isready -h 127.0.0.1 -U admin -d postgres",
            Psql + DockerEngineSql.IdentityQuery,
            Psql + "DROP DATABASE IF EXISTS \"Klacks\" WITH (FORCE)",
            Psql + "CREATE DATABASE \"Klacks\" WITH OWNER \"admin\" TEMPLATE template0 ENCODING 'UTF8' LC_COLLATE 'en_US.utf8' LC_CTYPE 'en_US.utf8' LOCALE_PROVIDER libc",
            "exec klacks-postgres pg_restore --exit-on-error -U admin -d dbname='Klacks' " + DumpDirectory + "/000.dump",
            Psql + "DROP DATABASE IF EXISTS \"klacks_diag\" WITH (FORCE)",
            Psql + "CREATE DATABASE \"klacks_diag\" WITH OWNER \"admin\" TEMPLATE template0 ENCODING 'UTF8' LC_COLLATE 'C' LC_CTYPE 'C' LOCALE_PROVIDER icu LOCALE 'und'",
            "exec klacks-postgres pg_restore --exit-on-error -U admin -d dbname='klacks_diag' " + DumpDirectory + "/001.dump",
            StartApi,
            RestartProxy,
        ]);
        ReadEnv().ShouldBe(
            ["POSTGRES_PASSWORD=secret", "KLACKS_POSTGRES_IMAGE=" + PinnedImage, "KLACKS_POSTGRES_DATA_SUBDIR=pg19", "KLACKS_API_TAG=1.0.78"]);
        context.NewState[DockerEngineStateKeys.Image].ShouldBe(PinnedImage);
        context.NewState[DockerEngineStateKeys.DataSubdir].ShouldBe("pg19");
        context.NewState[DockerEngineStateKeys.DumpDirectory].ShouldBe(DumpDirectory);
        _docker.RunningDataDirectory.ShouldBe("/var/lib/postgresql/data/pg19");
    }

    [Test]
    public async Task Activate_KeysMissing_AppendsBothKeysAndKeepsOtherLines()
    {
        File.WriteAllLines(EnvPath, MissingKeysEnv);

        await RunForwardAsync(CreateTarget(), StepsUntilActivate);

        ReadEnv().ShouldBe([.. MissingKeysEnv, "KLACKS_POSTGRES_IMAGE=" + PinnedImage, "KLACKS_POSTGRES_DATA_SUBDIR=pg19"]);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    public async Task Revert_FromEveryPhase_PinnedEnv_RestoresEnvAndOldCluster(int completedSteps)
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        var target = CreateTarget();
        var context = await RunForwardAsync(target, completedSteps);
        var callsBefore = _docker.Runner.Calls.Count;

        await target.RevertToOldEngineAsync(JournalOnly(context), CancellationToken.None);

        _docker.Runner.Calls.Skip(callsBefore).ShouldBe(ExpectedRevertCalls(completedSteps));
        ReadEnv().ShouldBe(PinnedSeventeenEnv);
        _docker.RunningDataDirectory.ShouldBe("/var/lib/postgresql/data/pgdata");
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    public async Task Revert_FromEveryPhase_MissingKeys_RemovesTheKeysAgain(int completedSteps)
    {
        File.WriteAllLines(EnvPath, MissingKeysEnv);
        var target = CreateTarget();
        var context = await RunForwardAsync(target, completedSteps);
        var callsBefore = _docker.Runner.Calls.Count;

        await target.RevertToOldEngineAsync(JournalOnly(context), CancellationToken.None);

        _docker.Runner.Calls.Skip(callsBefore).ShouldBe(ExpectedRevertCalls(completedSteps));
        ReadEnv().ShouldBe(MissingKeysEnv);
        _docker.RunningDataDirectory.ShouldBe("/var/lib/postgresql/data/pgdata");
    }

    [Test]
    public async Task Revert_Twice_IsIdempotent()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        var target = CreateTarget();
        var context = await RunForwardAsync(target, StepsUntilRestore);
        var journal = JournalOnly(context);

        var before = _docker.Runner.Calls.Count;
        await target.RevertToOldEngineAsync(journal, CancellationToken.None);
        var first = _docker.Runner.Calls.Skip(before).ToList();
        var envAfterFirst = ReadEnv();
        var middle = _docker.Runner.Calls.Count;
        await target.RevertToOldEngineAsync(journal, CancellationToken.None);

        first.ShouldBe(RevertCalls);
        _docker.Runner.Calls.Skip(middle).ShouldBe(RevertCallsWithoutOwnedTarget);
        ReadEnv().ShouldBe(envAfterFirst);
        ReadEnv().ShouldBe(PinnedSeventeenEnv);
    }

    [Test]
    public async Task Revert_StillOnTargetDirectory_RefusesToDeleteIt()
    {
        File.WriteAllLines(EnvPath, ["POSTGRES_PASSWORD=secret", "KLACKS_POSTGRES_DATA_SUBDIR=pg19"]);
        var context = new EngineUpgradeContext(OperationId, 17, 19, string.Empty, string.Empty, new Dictionary<string, string>(), new Dictionary<string, string>());

        await Should.ThrowAsync<InvalidOperationException>(() => CreateTarget().RevertToOldEngineAsync(context, CancellationToken.None));

        _docker.Runner.Calls.ShouldNotContain(call => call.Contains(" rm ", StringComparison.Ordinal));
    }

    [Test]
    public async Task Revert_OldStartFails_ThrowsWithoutDeletingTheTarget()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        var target = CreateTarget();
        var context = await RunForwardAsync(target, StepsUntilRestore);
        _docker.FailOn = UpPostgres;

        await Should.ThrowAsync<InvalidOperationException>(() => target.RevertToOldEngineAsync(JournalOnly(context), CancellationToken.None));

        _docker.Runner.Calls.ShouldNotContain(call => call.Contains(" rm ", StringComparison.Ordinal));
        ReadEnv().ShouldBe(PinnedSeventeenEnv);
    }

    [TestCase("docker.io/evil/postgres:19")]
    [TestCase("ghcr.io/heribertg/klacks-postgres-evil:19")]
    [TestCase("ghcr.io/heribertg/other/klacks-postgres:19")]
    public async Task PrepareArtifact_ForeignRepository_IsRejectedBeforeAnyPull(string artifactRef)
    {
        var context = new EngineUpgradeContext(OperationId, 17, 19, artifactRef, Digest, new Dictionary<string, string>(), new Dictionary<string, string>());

        await Should.ThrowAsync<InvalidOperationException>(() => CreateTarget().PrepareArtifactAsync(context, CancellationToken.None));

        _docker.Runner.Calls.ShouldBeEmpty();
    }

    [TestCase("")]
    [TestCase("sha256:XYZ")]
    [TestCase("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public async Task PrepareArtifact_MalformedDigest_IsRejectedBeforeAnyPull(string digest)
    {
        var context = new EngineUpgradeContext(OperationId, 17, 19, ArtifactRef, digest, new Dictionary<string, string>(), new Dictionary<string, string>());

        await Should.ThrowAsync<InvalidOperationException>(() => CreateTarget().PrepareArtifactAsync(context, CancellationToken.None));

        _docker.Runner.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task StopOldEngine_LeftoverTargetFolder_RefusesBeforeStoppingPostgres()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        _docker.VolumeEntries = "pgdata\npg19\n";

        await Should.ThrowAsync<InvalidOperationException>(() => RunForwardAsync(CreateTarget(), StepsUntilStopOld));

        _docker.Runner.Calls.ShouldNotContain("stop -t 120 klacks-postgres");
    }

    [Test]
    public async Task Restore_ReachesOldCluster_RefusesToDropDatabases()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        _docker.NewServerVersionNum = "170010";

        await Should.ThrowAsync<InvalidOperationException>(() => RunForwardAsync(CreateTarget(), StepsUntilRestore));

        _docker.Runner.Calls.ShouldNotContain(call => call.Contains("DROP DATABASE", StringComparison.Ordinal));
    }

    [Test]
    public async Task Restore_NewServerAcceptsTcpAfterRetries_ContinuesWithTheRestore()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        _docker.ReadinessFailures = 2;

        await RunForwardAsync(CreateTarget(), StepsUntilRestore);

        ReadinessProbeCount().ShouldBe(3);
        _docker.Runner.Calls.ShouldContain(call => call.Contains("DROP DATABASE", StringComparison.Ordinal));
    }

    [Test]
    public async Task Restore_NewServerNeverAcceptsTcp_FailsAfterAllAttemptsWithoutDroppingDatabases()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        _docker.ReadinessFailures = int.MaxValue;

        await Should.ThrowAsync<InvalidOperationException>(() => RunForwardAsync(CreateTarget(), StepsUntilRestore));

        ReadinessProbeCount().ShouldBe(DockerEngineUpgradeTarget.ReadinessAttempts);
        _docker.Runner.Calls.ShouldNotContain(call => call.Contains("DROP DATABASE", StringComparison.Ordinal));
    }

    [Test]
    public async Task Dump_DatabaseNameWithConninfoCharacters_IsPassedAsQuotedConninfo()
    {
        var context = new EngineUpgradeContext(OperationId, 17, 19, ArtifactRef, Digest, new Dictionary<string, string>(), new Dictionary<string, string>());
        EngineDatabase[] databases = [new("host=evil' x\\", "admin", "UTF8", "C", "C", "libc", null)];

        await CreateTarget().DumpAsync(context, databases, CancellationToken.None);

        _docker.Runner.Calls.ShouldContain("exec klacks-postgres pg_dump -Fc -U admin -d dbname='host=evil\\' x\\\\' -f " + DumpDirectory + "/000.dump");
    }

    [Test]
    public async Task Revert_ForeignTargetFolder_IsNeverDeleted()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        var target = CreateTarget();
        _docker.VolumeEntries = "pgdata\npg19\n";
        var context = ContextFor(await target.InspectAsync(CancellationToken.None));
        await target.PrepareArtifactAsync(context, CancellationToken.None);
        await target.DumpAsync(context, Databases, CancellationToken.None);
        await Should.ThrowAsync<InvalidOperationException>(() => target.StopOldEngineAsync(context, CancellationToken.None));
        var callsBefore = _docker.Runner.Calls.Count;

        await target.RevertToOldEngineAsync(JournalOnly(context), CancellationToken.None);

        _docker.Runner.Calls.Skip(callsBefore).ShouldBe(RevertCallsWithoutOwnedTarget);
        _docker.Runner.Calls.ShouldNotContain("stop -t 120 klacks-postgres");
    }

    [TestCase("${OTHER}")]
    [TestCase("a;b")]
    public async Task Revert_TamperedJournalValue_IsRefusedBeforeAnyChange(string value)
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        var context = new EngineUpgradeContext(OperationId, 17, 19, string.Empty, string.Empty,
            new Dictionary<string, string> { [DockerEngineStateKeys.EnvImage] = value }, new Dictionary<string, string>());

        await Should.ThrowAsync<InvalidOperationException>(() => CreateTarget().RevertToOldEngineAsync(context, CancellationToken.None));

        _docker.Runner.Calls.ShouldBeEmpty();
        ReadEnv().ShouldBe(PinnedSeventeenEnv);
    }

    [Test]
    public async Task Inspect_EnvValueLiterallyMissing_IsUnsupported()
    {
        File.WriteAllLines(EnvPath, ["POSTGRES_PASSWORD=secret", "KLACKS_POSTGRES_DATA_SUBDIR=missing"]);
        _docker.RunningDataDirectory = "/var/lib/postgresql/data/missing";

        var inspection = await CreateTarget().InspectAsync(CancellationToken.None);

        inspection.UnsupportedReason.ShouldNotBeNull();
    }

    [Test]
    public async Task Restore_DumpReferenceOutsideOperation_IsRefused()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        var target = CreateTarget();
        var context = await RunForwardAsync(target, StepsUntilActivate);
        IReadOnlyList<EngineDump> dumps = [new("Klacks", "/etc/shadow")];

        await Should.ThrowAsync<InvalidOperationException>(() => target.RestoreAsync(context, dumps, CancellationToken.None));

        _docker.Runner.Calls.ShouldNotContain(call => call.Contains("pg_restore", StringComparison.Ordinal));
    }

    [TestCase(StepsUntilDump)]
    [TestCase(StepsUntilActivate)]
    [TestCase(StepsUntilRestore)]
    public async Task Revert_JournalWithOldStateOnly_RestoresEnvAndDeletesTarget(int completedSteps)
    {
        File.WriteAllLines(EnvPath, MissingKeysEnv);
        var target = CreateTarget();
        var context = await RunForwardAsync(target, completedSteps);
        var callsBefore = _docker.Runner.Calls.Count;
        var journal = new EngineUpgradeContext(OperationId, 17, 19, string.Empty, string.Empty,
            new Dictionary<string, string>(context.OldState), new Dictionary<string, string>());

        await target.RevertToOldEngineAsync(journal, CancellationToken.None);

        _docker.Runner.Calls.Skip(callsBefore).ShouldBe(ExpectedRevertCalls(completedSteps));
        ReadEnv().ShouldBe(MissingKeysEnv);
    }

    [Test]
    public async Task Revert_OldDataDirectoryHoldsOtherMajor_RefusesToDelete()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        var target = CreateTarget();
        var context = await RunForwardAsync(target, StepsUntilRestore);
        _docker.OldMajor = "16\n";

        await Should.ThrowAsync<InvalidOperationException>(() => target.RevertToOldEngineAsync(JournalOnly(context), CancellationToken.None));

        _docker.Runner.Calls.ShouldNotContain(call => call.Contains(" rm ", StringComparison.Ordinal));
    }

    [Test]
    public async Task Activate_WithoutRecordedOldEnvValues_RefusesToChangeEnv()
    {
        File.WriteAllLines(EnvPath, MissingKeysEnv);
        var context = new EngineUpgradeContext(OperationId, 17, 19, ArtifactRef, Digest, new Dictionary<string, string>(), new Dictionary<string, string>());

        await Should.ThrowAsync<InvalidOperationException>(() => CreateTarget().ActivateNewEngineAsync(context, CancellationToken.None));

        ReadEnv().ShouldBe(MissingKeysEnv);
        _docker.Runner.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Commit_WithoutPinnedImage_RefusesToChangeEnv()
    {
        File.WriteAllLines(EnvPath, MissingKeysEnv);
        var context = new EngineUpgradeContext(OperationId, 17, 19, string.Empty, string.Empty, new Dictionary<string, string>(),
            new Dictionary<string, string> { [DockerEngineStateKeys.Image] = "ghcr.io/heribertg/klacks-postgres:19" });

        await Should.ThrowAsync<InvalidOperationException>(() => CreateTarget().CommitConfigurationAsync(context, CancellationToken.None));

        ReadEnv().ShouldBe(MissingKeysEnv);
    }

    [Test]
    public async Task Restore_PgRestoreFailsForSecondDatabase_ThenRevertRestoresOldCluster()
    {
        File.WriteAllLines(EnvPath, PinnedSeventeenEnv);
        var target = CreateTarget();
        _docker.FailOn = "exec klacks-postgres pg_restore --exit-on-error -U admin -d dbname='klacks_diag'";

        await Should.ThrowAsync<InvalidOperationException>(() => RunForwardAsync(target, StepsUntilRestore));
        _docker.FailOn = null;
        var callsBefore = _docker.Runner.Calls.Count;
        var inspection = new EngineInspection(17, new Dictionary<string, string>
        {
            [DockerEngineStateKeys.EnvImage] = "pgvector/pgvector:pg17",
            [DockerEngineStateKeys.EnvDataSubdir] = "pgdata",
            [DockerEngineStateKeys.Image] = "pgvector/pgvector:pg17",
            [DockerEngineStateKeys.DataSubdir] = "pgdata",
        }, 0, null);

        await target.RevertToOldEngineAsync(ContextFor(inspection), CancellationToken.None);

        _docker.Runner.Calls.Skip(callsBefore).ShouldBe(RevertCalls);
        ReadEnv().ShouldBe(PinnedSeventeenEnv);
        _docker.RunningDataDirectory.ShouldBe("/var/lib/postgresql/data/pgdata");
    }

    [Test]
    public async Task Inspect_DockerExecFails_Throws()
    {
        File.WriteAllLines(EnvPath, MissingKeysEnv);
        _docker.FailOn = PrintDataDirectory;

        await Should.ThrowAsync<InvalidOperationException>(() => CreateTarget().InspectAsync(CancellationToken.None));
    }

    [Test]
    public async Task ApplicationStartAndStop_AreIdempotentComposeCalls()
    {
        var target = CreateTarget();

        await target.StopApplicationAsync(CancellationToken.None);
        await target.StopApplicationAsync(CancellationToken.None);
        await target.StartApplicationAsync(CancellationToken.None);
        await target.StartApplicationAsync(CancellationToken.None);

        _docker.Runner.Calls.ShouldBe([StopApi, StopApi, StartApi, RestartProxy, StartApi, RestartProxy]);
    }

    [Test]
    public async Task StartApplication_ProxyRestartFails_DoesNotThrow()
    {
        _docker.FailOn = RestartProxy;

        await CreateTarget().StartApplicationAsync(CancellationToken.None);

        _docker.Runner.Calls.ShouldBe([StartApi, RestartProxy]);
    }

    [Test]
    public async Task StartApplication_NoProxyService_StartsOnlyTheApi()
    {
        _options.ProxyServiceName = string.Empty;

        await CreateTarget().StartApplicationAsync(CancellationToken.None);

        _docker.Runner.Calls.ShouldBe([StartApi]);
    }
}
