// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class WorkerStartupTests
{
    private static readonly TimeSpan PollingTimeout = TimeSpan.FromSeconds(10);

    private IUpdateOperationStore _store = null!;
    private IInstalledVersionReconciler _reconciler = null!;
    private TaskCompletionSource _polling = null!;
    private EngineUpgradeHarness _engine = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IUpdateOperationStore>();
        _store.ReclaimStuckAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(0);
        _polling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.ClaimNextPendingAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _polling.TrySetResult();
            return Task.FromResult<UpdateOperation?>(null);
        });
        _reconciler = Substitute.For<IInstalledVersionReconciler>();
        _engine = new EngineUpgradeHarness();
    }

    [TearDown]
    public void TearDown() => _engine.Dispose();

    private Worker CreateWorker()
    {
        var executor = new UpdateExecutor(
            Substitute.For<IUpdateApplier>(),
            Substitute.For<IUpdateBackupService>(),
            Substitute.For<IUpdateHealthGate>(),
            Substitute.For<IArtifactVerifier>(),
            NullLogger<UpdateExecutor>.Instance);
        var whisperExecutor = new WhisperPluginExecutor(
            Substitute.For<IWhisperPluginApplier>(),
            Substitute.For<IWhisperModelInstaller>(),
            Substitute.For<IWhisperProviderStore>(),
            Substitute.For<IWhisperResourceCheck>(),
            Substitute.For<ILocalTtsStore>(),
            Options.Create(new UpdaterOptions()),
            NullLogger<WhisperPluginExecutor>.Instance);
        var ttsExecutor = new TtsInstallExecutor(
            Substitute.For<IWhisperPluginApplier>(),
            Substitute.For<IWhisperModelInstaller>(),
            Substitute.For<ILocalTtsStore>(),
            Substitute.For<IWhisperProviderStore>(),
            Options.Create(new UpdaterOptions()),
            NullLogger<TtsInstallExecutor>.Instance);
        return new Worker(
            _store,
            executor,
            whisperExecutor,
            ttsExecutor,
            _engine.Executor,
            _engine.Recovery,
            Substitute.For<IBackupRetentionService>(),
            _reconciler,
            NullLogger<Worker>.Instance);
    }

    [Test]
    public async Task Startup_reconciles_the_installed_version_once_before_polling()
    {
        using var worker = CreateWorker();

        await worker.StartAsync(CancellationToken.None);
        await _polling.Task.WaitAsync(PollingTimeout);
        using var stopTimeout = new CancellationTokenSource(PollingTimeout);
        await worker.StopAsync(stopTimeout.Token);

        Received.InOrder(() =>
        {
            _reconciler.Reconcile();
            _store.ReclaimStuckAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Startup_keeps_running_when_reconciling_fails()
    {
        _reconciler.When(reconciler => reconciler.Reconcile()).Do(_ => throw new InvalidOperationException("boom"));
        using var worker = CreateWorker();

        await worker.StartAsync(CancellationToken.None);
        await _polling.Task.WaitAsync(PollingTimeout);
        using var stopTimeout = new CancellationTokenSource(PollingTimeout);
        await worker.StopAsync(stopTimeout.Token);

        await _store.Received(1).ReclaimStuckAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Startup_recovers_an_open_engine_upgrade_before_reclaiming_stuck_rows()
    {
        await _engine.Journal.SaveAsync(
            new Klacks.Updater.Domain.EngineUpgrade.EngineUpgradeJournalEntry
            {
                Schema = 1,
                OperationId = Guid.NewGuid(),
                FromMajor = 17,
                ToMajor = 19,
                Phase = Klacks.Updater.Domain.EngineUpgrade.EngineUpgradePhase.Restored,
                Old = new Dictionary<string, string> { ["oldImage"] = "pg17" },
                AppVersion = EngineUpgradeHarness.AppVersion,
            },
            CancellationToken.None);
        using var worker = CreateWorker();

        await worker.StartAsync(CancellationToken.None);
        await _polling.Task.WaitAsync(PollingTimeout);
        using var stopTimeout = new CancellationTokenSource(PollingTimeout);
        await worker.StopAsync(stopTimeout.Token);

        Received.InOrder(() =>
        {
            _engine.Store.CompleteAsync(Arg.Any<Guid>(), UpdateExecutionStatus.RolledBack, Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
            _store.ReclaimStuckAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        });
        _engine.Target.Calls.ShouldContain(FakeEngineUpgradeTarget.Revert);
    }
}
