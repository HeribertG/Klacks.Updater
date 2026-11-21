// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure.EngineUpgrade;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class WorkerRoutingTests
{
    private const int UnknownOperationType = 99;
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

    private IUpdateOperationStore _store = null!;
    private IUpdateApplier _applier = null!;
    private IWhisperPluginApplier _whisperApplier = null!;
    private EngineUpgradeHarness _engine = null!;
    private TaskCompletionSource<(UpdateExecutionStatus Status, string Message)> _completed = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IUpdateOperationStore>();
        _applier = Substitute.For<IUpdateApplier>();
        _whisperApplier = Substitute.For<IWhisperPluginApplier>();
        _engine = new EngineUpgradeHarness();
        _engine.Target.Inspection = _engine.Target.Inspection with { UnsupportedReason = UnsupportedEngineUpgradeTarget.Reason };
        _completed = new TaskCompletionSource<(UpdateExecutionStatus, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.CompleteAsync(Arg.Any<Guid>(), Arg.Any<UpdateExecutionStatus>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _completed.TrySetResult((call.ArgAt<UpdateExecutionStatus>(1), call.ArgAt<string>(2)));
                return Task.CompletedTask;
            });
    }

    [TearDown]
    public void TearDown() => _engine.Dispose();

    private async Task<(UpdateExecutionStatus Status, string Message)> RunAsync(UpdateOperationType type, TimeSpan? queuedFor = null)
    {
        var operation = new UpdateOperation
        {
            Id = Guid.NewGuid(),
            OperationType = type,
            FromVersion = "17",
            TargetVersion = "19",
            ArtifactRef = "ghcr.io/heribertg/klacks-postgres:19",
            ArtifactSha256 = "sha256:abc",
            RequestedAt = DateTime.UtcNow - (queuedFor ?? TimeSpan.Zero),
        };
        _store.ClaimNextPendingAsync(Arg.Any<CancellationToken>()).Returns(operation, (UpdateOperation?)null);

        var executor = new UpdateExecutor(
            _applier,
            Substitute.For<IUpdateBackupService>(),
            Substitute.For<IUpdateHealthGate>(),
            Substitute.For<IArtifactVerifier>(),
            NullLogger<UpdateExecutor>.Instance);
        var whisperExecutor = new WhisperPluginExecutor(
            _whisperApplier,
            Substitute.For<IWhisperModelInstaller>(),
            Substitute.For<IWhisperProviderStore>(),
            Substitute.For<IWhisperResourceCheck>(),
            Substitute.For<ILocalTtsStore>(),
            Options.Create(new UpdaterOptions()),
            NullLogger<WhisperPluginExecutor>.Instance);
        var ttsExecutor = new TtsInstallExecutor(
            _whisperApplier,
            Substitute.For<IWhisperModelInstaller>(),
            Substitute.For<ILocalTtsStore>(),
            Substitute.For<IWhisperProviderStore>(),
            Options.Create(new UpdaterOptions()),
            NullLogger<TtsInstallExecutor>.Instance);
        using var worker = new Worker(
            _store,
            executor,
            whisperExecutor,
            ttsExecutor,
            _engine.Executor,
            _engine.Recovery,
            Substitute.For<IBackupRetentionService>(),
            Substitute.For<IInstalledVersionReconciler>(),
            NullLogger<Worker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        var result = await _completed.Task.WaitAsync(CompletionTimeout);
        using var stopTimeout = new CancellationTokenSource(CompletionTimeout);
        await worker.StopAsync(stopTimeout.Token);
        return result;
    }

    [Test]
    public async Task Postgres_upgrade_goes_to_the_engine_executor_and_never_touches_the_application()
    {
        var (status, _) = await RunAsync(UpdateOperationType.PostgresUpgrade);

        status.ShouldBe(UpdateExecutionStatus.Failed);
        _engine.Target.Calls.ShouldBe([FakeEngineUpgradeTarget.Inspect]);
        _applier.ReceivedCalls().ShouldBeEmpty();
        _whisperApplier.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task Tts_install_goes_to_the_tts_executor_and_never_touches_the_application()
    {
        var (status, message) = await RunAsync(UpdateOperationType.TtsInstall, TimeSpan.FromDays(3));

        status.ShouldBe(UpdateExecutionStatus.Failed);
        message.ShouldContain("local speech model");
        _engine.Target.Calls.ShouldBeEmpty();
        _applier.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task Expired_postgres_upgrade_request_fails_without_touching_the_engine()
    {
        var (status, message) = await RunAsync(UpdateOperationType.PostgresUpgrade, TimeSpan.FromMinutes(31));

        status.ShouldBe(UpdateExecutionStatus.Failed);
        message.ShouldBe(EngineUpgradeConstants.ExpiredRequestMessage);
        _engine.Target.Calls.ShouldBeEmpty();
        _applier.ReceivedCalls().ShouldBeEmpty();
        _whisperApplier.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task Old_application_update_request_is_not_expired()
    {
        var (_, message) = await RunAsync(UpdateOperationType.Update, TimeSpan.FromHours(5));

        message.ShouldNotBe(EngineUpgradeConstants.ExpiredRequestMessage);
    }

    [Test]
    public async Task Unknown_operation_type_fails_without_any_change()
    {
        var (status, message) = await RunAsync((UpdateOperationType)UnknownOperationType);

        status.ShouldBe(UpdateExecutionStatus.Failed);
        message.ShouldContain(UnknownOperationType.ToString());
        _applier.ReceivedCalls().ShouldBeEmpty();
        _whisperApplier.ReceivedCalls().ShouldBeEmpty();
        _engine.Target.Calls.ShouldBeEmpty();
    }
}
