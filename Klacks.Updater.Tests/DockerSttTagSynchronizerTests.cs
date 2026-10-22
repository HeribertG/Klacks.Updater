// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class DockerSttTagSynchronizerTests
{
    private const string InspectPrefix = "image inspect --format";
    private const string PullStt = "compose --profile parakeet pull klacks-stt";
    private const string UpStt = "compose --profile parakeet up -d klacks-stt";

    private string _dir = null!;
    private UpdaterOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "klacks-updater-stt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _options = new UpdaterOptions { ComposeProjectDir = _dir };
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_dir, recursive: true);
    }

    private DockerSttTagSynchronizer Create(RecordingProcessRunner runner) =>
        new(Options.Create(_options), runner, NullLogger<DockerSttTagSynchronizer>.Instance);

    private void WriteEnv(params string[] lines) => File.WriteAllLines(Path.Combine(_dir, ".env"), lines);

    [Test]
    public async Task Plan_InspectsPulledApiImageWithLabelFormat()
    {
        var runner = new RecordingProcessRunner(_ => "1.2.0\n");

        await Create(runner).PlanAsync("1.0.50");

        runner.Calls.Count.ShouldBe(1);
        runner.Calls[0].ShouldBe(
            "image inspect --format {{ index .Config.Labels \"org.klacks.stt-version\" }} ghcr.io/heribertg/klacks-api:1.0.50");
    }

    [Test]
    public async Task Plan_ValidLabel_ReturnsTagWithRecordedPreviousTagAndActiveProfile()
    {
        WriteEnv("KLACKS_API_TAG=1.0.49", "KLACKS_STT_TAG=1.0.0", "COMPOSE_PROFILES=parakeet");
        var runner = new RecordingProcessRunner(_ => "1.2.0\n");

        var update = await Create(runner).PlanAsync("1.0.50");

        update.ShouldBe(new SttTagUpdate("1.2.0", "1.0.0", IsServiceActive: true));
        update!.RequiresRestart.ShouldBeTrue();
    }

    [Test]
    public async Task Plan_NoEnvFile_PreviousTagUnknownAndServiceInactive()
    {
        var runner = new RecordingProcessRunner(_ => "1.2.0");

        var update = await Create(runner).PlanAsync("1.0.50");

        update.ShouldBe(new SttTagUpdate("1.2.0", null, IsServiceActive: false));
        update!.RequiresRestart.ShouldBeFalse();
    }

    [Test]
    public async Task Plan_WhisperProfileOnly_ServiceInactive()
    {
        WriteEnv("COMPOSE_PROFILES=whisper");
        var runner = new RecordingProcessRunner(_ => "1.2.0");

        var update = await Create(runner).PlanAsync("1.0.50");

        update!.IsServiceActive.ShouldBeFalse();
    }

    [TestCase("")]
    [TestCase("\n")]
    [TestCase("<no value>")]
    [TestCase("latest")]
    [TestCase("1.2")]
    [TestCase("1.2.0-rc1")]
    public async Task Plan_MissingOrInvalidLabel_ReturnsNull(string output)
    {
        WriteEnv("KLACKS_STT_TAG=1.0.0", "COMPOSE_PROFILES=parakeet");
        var runner = new RecordingProcessRunner(_ => output);

        var update = await Create(runner).PlanAsync("1.0.50");

        update.ShouldBeNull();
    }

    [Test]
    public async Task Plan_InspectFails_ReturnsNullWithoutThrowing()
    {
        var runner = new RecordingProcessRunner(_ => throw new InvalidOperationException("docker exploded"));

        var update = await Create(runner).PlanAsync("1.0.50");

        update.ShouldBeNull();
    }

    [Test]
    public async Task Plan_Cancelled_Propagates()
    {
        var runner = new RecordingProcessRunner(_ => throw new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => Create(runner).PlanAsync("1.0.50"));
    }

    [Test]
    public async Task Apply_ActiveAndTagChanged_PullsThenUpsSttServiceWithEnvironment()
    {
        var runner = new RecordingProcessRunner();
        var environment = new Dictionary<string, string> { ["KLACKS_STT_TAG"] = "1.2.0" };

        await Create(runner).ApplyAsync(new SttTagUpdate("1.2.0", "1.0.0", IsServiceActive: true), environment);

        runner.Calls.ShouldBe([PullStt, UpStt]);
        runner.Environments.ShouldAllBe(e => e["KLACKS_STT_TAG"] == "1.2.0");
    }

    [Test]
    public async Task Apply_ActiveAndPreviousTagUnknown_Restarts()
    {
        var runner = new RecordingProcessRunner();

        await Create(runner).ApplyAsync(new SttTagUpdate("1.2.0", null, IsServiceActive: true), new Dictionary<string, string>());

        runner.Calls.ShouldBe([PullStt, UpStt]);
    }

    [Test]
    public async Task Apply_TagUnchanged_DoesNothing()
    {
        var runner = new RecordingProcessRunner();

        await Create(runner).ApplyAsync(new SttTagUpdate("1.2.0", "1.2.0", IsServiceActive: true), new Dictionary<string, string>());

        runner.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Apply_ServiceInactive_DoesNothing()
    {
        var runner = new RecordingProcessRunner();

        await Create(runner).ApplyAsync(new SttTagUpdate("1.2.0", "1.0.0", IsServiceActive: false), new Dictionary<string, string>());

        runner.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Apply_PullFails_IsSwallowedAndServiceIsNotStarted()
    {
        var runner = new RecordingProcessRunner(call => call == PullStt
            ? throw new InvalidOperationException("pull denied")
            : string.Empty);

        await Create(runner).ApplyAsync(new SttTagUpdate("1.2.0", "1.0.0", IsServiceActive: true), new Dictionary<string, string>());

        runner.Calls.ShouldBe([PullStt]);
    }

    [Test]
    public async Task Apply_UpFails_IsSwallowed()
    {
        var runner = new RecordingProcessRunner(call => call == UpStt
            ? throw new InvalidOperationException("no such service")
            : string.Empty);

        await Create(runner).ApplyAsync(new SttTagUpdate("1.2.0", "1.0.0", IsServiceActive: true), new Dictionary<string, string>());

        runner.Calls.ShouldBe([PullStt, UpStt]);
    }

    [Test]
    public async Task Apply_UsesConfiguredServiceAndProfileNames()
    {
        _options.SttServiceName = "custom-stt";
        _options.SttComposeProfile = "custom";
        var runner = new RecordingProcessRunner();

        await Create(runner).ApplyAsync(new SttTagUpdate("1.2.0", "1.0.0", IsServiceActive: true), new Dictionary<string, string>());

        runner.Calls.ShouldBe(["compose --profile custom pull custom-stt", "compose --profile custom up -d custom-stt"]);
    }
}
