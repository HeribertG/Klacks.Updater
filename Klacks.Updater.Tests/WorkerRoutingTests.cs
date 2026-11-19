// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
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
    private IEngineUpgradeTarget _engineTarget = null!;
    private TaskCompletionSource<(UpdateExecutionStatus Status, string Message)> _completed = null!;

    [SetUp]
    public void SetUp()
    {
        _store = Substitute.For<IUpdateOperationStore>();
        _applier = Substitute.For<IUpdateApplier>();
        _whisperApplier = Substitute.For<IWhisperPluginApplier>();
        _engineTarget = Substitute.For<IEngineUpgradeTarget>();
        _engineTarget.InspectAsync(Arg.Any<CancellationToken>())
            .Returns(new EngineInspection(0, new Dictionary<string, string>(), 0, UnsupportedEngineUpgradeTarget.Reason));
        _completed = new TaskCompletionSource<(UpdateExecutionStatus, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _store.CompleteAsync(Arg.Any<Guid>(), Arg.Any<UpdateExecutionStatus>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _completed.TrySetResult((call.ArgAt<UpdateExecutionStatus>(1), call.ArgAt<string>(2)));
                return Task.CompletedTask;
            });
    }

    private async Task<(UpdateExecutionStatus Status, string Message)> RunAsync(UpdateOperationType type)
    {
        var operation = new UpdateOperation
        {
            Id = Guid.NewGuid(),
            OperationType = type,
            FromVersion = "17",
            TargetVersion = "19",
            ArtifactRef = "ghcr.io/heribertg/klacks-postgres:19",
            ArtifactSha256 = "sha256:abc",
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
            Options.Create(new UpdaterOptions()),
            NullLogger<WhisperPluginExecutor>.Instance);
        using var worker = new Worker(
            _store,
            executor,
            whisperExecutor,
            new EngineUpgradeExecutor(_engineTarget, NullLogger<EngineUpgradeExecutor>.Instance),
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
        await _engineTarget.Received(1).InspectAsync(Arg.Any<CancellationToken>());
        _applier.ReceivedCalls().ShouldBeEmpty();
        _whisperApplier.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task Unknown_operation_type_fails_without_any_change()
    {
        var (status, message) = await RunAsync((UpdateOperationType)UnknownOperationType);

        status.ShouldBe(UpdateExecutionStatus.Failed);
        message.ShouldContain(UnknownOperationType.ToString());
        _applier.ReceivedCalls().ShouldBeEmpty();
        _whisperApplier.ReceivedCalls().ShouldBeEmpty();
        _engineTarget.ReceivedCalls().ShouldBeEmpty();
    }
}
