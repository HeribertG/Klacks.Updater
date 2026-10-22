// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class DockerApplierTests
{
    private const string PullStt = "compose --profile parakeet pull klacks-stt";
    private const string UpStt = "compose --profile parakeet up -d klacks-stt";
    private const string InspectCall = "image inspect";

    private string _dir = null!;
    private UpdaterOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "klacks-updater-docker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _options = new UpdaterOptions { ComposeProjectDir = _dir, UiImageRepository = "ghcr.io/heribertg/klacks-ui" };
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_dir, recursive: true);
    }

    private string EnvPath => Path.Combine(_dir, ".env");

    private string[] ReadEnv() => File.ReadAllLines(EnvPath);

    private DockerApplier Create(RecordingProcessRunner runner, ISttTagSynchronizer? synchronizer = null) =>
        new(
            Options.Create(_options),
            runner,
            synchronizer ?? new DockerSttTagSynchronizer(Options.Create(_options), runner, NullLogger<DockerSttTagSynchronizer>.Instance),
            NullLogger<DockerApplier>.Instance);

    private static RecordingProcessRunner RunnerWithLabel(string label, Func<string, string>? other = null) =>
        new(call => call.StartsWith(InspectCall, StringComparison.Ordinal) ? label : other?.Invoke(call) ?? string.Empty);

    [Test]
    public async Task Activate_LabelPresent_PersistsSttTagNextToApiAndUiTags()
    {
        File.WriteAllLines(EnvPath, ["POSTGRES_PASSWORD=secret", "KLACKS_STT_TAG=1.0.0"]);
        var runner = RunnerWithLabel("1.1.0\n");

        await Create(runner).ActivateVersionAsync("1.0.50");

        ReadEnv().ShouldBe(["POSTGRES_PASSWORD=secret", "KLACKS_STT_TAG=1.1.0", "KLACKS_API_TAG=1.0.50", "KLACKS_UI_TAG=1.0.50"], ignoreOrder: true);
    }

    [Test]
    public async Task Activate_LabelPresent_ReadsLabelAfterApiPullBeforeApiUp()
    {
        var runner = RunnerWithLabel("1.1.0");

        await Create(runner).ActivateVersionAsync("1.0.50");

        runner.Calls[0].ShouldBe("compose pull klacks-api");
        runner.Calls[1].ShouldStartWith(InspectCall);
        runner.Calls[2].ShouldBe("compose up -d klacks-api");
        runner.Environments[2]["KLACKS_STT_TAG"].ShouldBe("1.1.0");
    }

    [Test]
    public async Task Activate_NoLabel_LeavesSttTagUntouchedAndDoesNotTouchSttService()
    {
        File.WriteAllLines(EnvPath, ["KLACKS_STT_TAG=1.0.0", "COMPOSE_PROFILES=parakeet"]);
        var runner = RunnerWithLabel(string.Empty);

        await Create(runner).ActivateVersionAsync("1.0.50");

        ReadEnv().ShouldContain("KLACKS_STT_TAG=1.0.0");
        ReadEnv().ShouldContain("KLACKS_API_TAG=1.0.50");
        runner.Calls.ShouldNotContain(PullStt);
        runner.Calls.ShouldNotContain(UpStt);
        runner.Environments.ShouldAllBe(e => !e.ContainsKey("KLACKS_STT_TAG"));
    }

    [Test]
    public async Task Activate_NoLabelAndNoEnvEntry_DoesNotCreateSttTag()
    {
        var runner = RunnerWithLabel("<no value>");

        await Create(runner).ActivateVersionAsync("1.0.50");

        ReadEnv().ShouldNotContain(line => line.StartsWith("KLACKS_STT_TAG", StringComparison.Ordinal));
    }

    [Test]
    public async Task Activate_ActiveSttAndTagChanged_RestartsSttAfterApiAndUi()
    {
        File.WriteAllLines(EnvPath, ["KLACKS_STT_TAG=1.0.0", "COMPOSE_PROFILES=parakeet"]);
        var runner = RunnerWithLabel("1.1.0");

        await Create(runner).ActivateVersionAsync("1.0.50");

        var calls = runner.Calls;
        calls.IndexOf(PullStt).ShouldBeGreaterThan(calls.IndexOf("compose up -d klacks-ui"));
        calls.IndexOf(UpStt).ShouldBe(calls.IndexOf(PullStt) + 1);
        runner.Environments[calls.IndexOf(UpStt)]["KLACKS_STT_TAG"].ShouldBe("1.1.0");
        ReadEnv().ShouldContain("KLACKS_STT_TAG=1.1.0");
    }

    [Test]
    public async Task Activate_ActiveSttAndTagUnchanged_DoesNotRestartStt()
    {
        File.WriteAllLines(EnvPath, ["KLACKS_STT_TAG=1.1.0", "COMPOSE_PROFILES=parakeet"]);
        var runner = RunnerWithLabel("1.1.0");

        await Create(runner).ActivateVersionAsync("1.0.50");

        runner.Calls.ShouldNotContain(PullStt);
        runner.Calls.ShouldNotContain(UpStt);
    }

    [Test]
    public async Task Activate_SttInactiveAndTagChanged_PersistsTagButDoesNotStartStt()
    {
        File.WriteAllLines(EnvPath, ["KLACKS_STT_TAG=1.0.0"]);
        var runner = RunnerWithLabel("1.1.0");

        await Create(runner).ActivateVersionAsync("1.0.50");

        ReadEnv().ShouldContain("KLACKS_STT_TAG=1.1.0");
        runner.Calls.ShouldNotContain(PullStt);
        runner.Calls.ShouldNotContain(UpStt);
    }

    [Test]
    public async Task Activate_SttRestartFails_DoesNotFailTheApiUpdateAndKeepsPersistedTags()
    {
        File.WriteAllLines(EnvPath, ["KLACKS_STT_TAG=1.0.0", "COMPOSE_PROFILES=parakeet"]);
        var runner = RunnerWithLabel("1.1.0", call => call == PullStt
            ? throw new InvalidOperationException("registry unreachable")
            : string.Empty);

        await Should.NotThrowAsync(() => Create(runner).ActivateVersionAsync("1.0.50"));

        ReadEnv().ShouldContain("KLACKS_API_TAG=1.0.50");
        ReadEnv().ShouldContain("KLACKS_STT_TAG=1.1.0");
    }

    [Test]
    public async Task Activate_InspectFails_DoesNotFailTheApiUpdate()
    {
        var runner = new RecordingProcessRunner(call => call.StartsWith(InspectCall, StringComparison.Ordinal)
            ? throw new InvalidOperationException("inspect failed")
            : string.Empty);

        await Should.NotThrowAsync(() => Create(runner).ActivateVersionAsync("1.0.50"));

        runner.Calls.ShouldContain("compose up -d klacks-api");
        ReadEnv().ShouldContain("KLACKS_API_TAG=1.0.50");
    }

    [Test]
    public async Task Activate_ApiUpFails_PropagatesAndNeverTouchesStt()
    {
        File.WriteAllLines(EnvPath, ["KLACKS_STT_TAG=1.0.0", "COMPOSE_PROFILES=parakeet"]);
        var runner = RunnerWithLabel("1.1.0", call => call == "compose up -d klacks-api"
            ? throw new InvalidOperationException("api failed")
            : string.Empty);

        await Should.ThrowAsync<InvalidOperationException>(() => Create(runner).ActivateVersionAsync("1.0.50"));

        runner.Calls.ShouldNotContain(PullStt);
        ReadEnv().ShouldContain("KLACKS_STT_TAG=1.0.0");
    }

    [Test]
    public async Task Activate_UsesConfiguredSttTagEnvVar()
    {
        _options.SttTagEnvVar = "CUSTOM_STT_TAG";
        var runner = RunnerWithLabel("1.1.0");

        await Create(runner).ActivateVersionAsync("1.0.50");

        ReadEnv().ShouldContain("CUSTOM_STT_TAG=1.1.0");
    }

    [Test]
    public async Task Activate_AppliesSynchronizerPlanWithTheFullEnvironment()
    {
        var update = new SttTagUpdate("1.1.0", "1.0.0", IsServiceActive: true);
        var synchronizer = Substitute.For<ISttTagSynchronizer>();
        synchronizer.PlanAsync("1.0.50", Arg.Any<CancellationToken>()).Returns(update);
        var runner = new RecordingProcessRunner();

        await Create(runner, synchronizer).ActivateVersionAsync("1.0.50");

        await synchronizer.Received(1).ApplyAsync(
            update,
            Arg.Is<IDictionary<string, string>>(e => e != null &&
                e["KLACKS_API_TAG"] == "1.0.50" && e["KLACKS_UI_TAG"] == "1.0.50" && e["KLACKS_STT_TAG"] == "1.1.0"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Activate_NoPlan_DoesNotApply()
    {
        var synchronizer = Substitute.For<ISttTagSynchronizer>();
        synchronizer.PlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SttTagUpdate?)null);
        var runner = new RecordingProcessRunner();

        await Create(runner, synchronizer).ActivateVersionAsync("1.0.50");

        await synchronizer.DidNotReceiveWithAnyArgs().ApplyAsync(default!, default!, default);
    }
}
