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

[TestFixture]
public class WorkerStartupTests
{
    private static readonly TimeSpan PollingTimeout = TimeSpan.FromSeconds(10);

    private IUpdateOperationStore _store = null!;
    private IInstalledVersionReconciler _reconciler = null!;
    private TaskCompletionSource _polling = null!;

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
    }

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
            Options.Create(new UpdaterOptions()),
            NullLogger<WhisperPluginExecutor>.Instance);
        return new Worker(
            _store,
            executor,
            whisperExecutor,
            new EngineUpgradeExecutor(new UnsupportedEngineUpgradeTarget(), NullLogger<EngineUpgradeExecutor>.Instance),
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
}
