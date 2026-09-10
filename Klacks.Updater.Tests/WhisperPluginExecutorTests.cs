// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class WhisperPluginExecutorTests
{
    private const string LargeModelId = "deepdml/faster-whisper-large-v3-turbo-ct2";
    private const string SmallModelId = "Systran/faster-whisper-small";

    private IWhisperPluginApplier _applier = null!;
    private IWhisperModelInstaller _installer = null!;
    private IWhisperProviderStore _providerStore = null!;
    private IWhisperResourceCheck _resourceCheck = null!;
    private WhisperPluginExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _applier = Substitute.For<IWhisperPluginApplier>();
        _installer = Substitute.For<IWhisperModelInstaller>();
        _providerStore = Substitute.For<IWhisperProviderStore>();
        _resourceCheck = Substitute.For<IWhisperResourceCheck>();
        _resourceCheck.CheckAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _installer.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(true);
        _executor = new WhisperPluginExecutor(_applier, _installer, _providerStore, _resourceCheck, NullLogger<WhisperPluginExecutor>.Instance);
    }

    private static UpdateOperation InstallOp(string alias = "large-v3-turbo", string? modelId = LargeModelId) => new()
    {
        Id = Guid.NewGuid(),
        OperationType = UpdateOperationType.WhisperInstall,
        TargetVersion = alias,
        ArtifactRef = modelId,
    };

    private static UpdateOperation UninstallOp() => new()
    {
        Id = Guid.NewGuid(),
        OperationType = UpdateOperationType.WhisperUninstall,
        TargetVersion = "large-v3-turbo",
    };

    [Test]
    public async Task Install_succeeds_and_enables_provider()
    {
        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        await _applier.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await _installer.Received(1).InstallModelAsync(LargeModelId, Arg.Any<CancellationToken>());
        await _providerStore.Received(1).UpsertEnabledAsync(LargeModelId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_fails_without_model_reference()
    {
        var result = await _executor.ExecuteAsync(InstallOp(modelId: null));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_fails_on_capacity_rejection_without_starting_container()
    {
        _resourceCheck.CheckAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns("Not enough free memory");

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldBe("Not enough free memory");
        await _applier.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_fails_when_service_never_ready_and_skips_model_install()
    {
        _installer.WaitForReadyAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _installer.DidNotReceive().InstallModelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _providerStore.DidNotReceive().UpsertEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_fails_when_model_install_throws_and_keeps_container_running()
    {
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("download failed"));

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("download failed");
        await _providerStore.DidNotReceive().UpsertEnabledAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_uses_small_requirements_for_small_alias()
    {
        await _executor.ExecuteAsync(InstallOp(alias: WhisperPluginConstants.ModelAliasSmall, modelId: SmallModelId));

        await _resourceCheck.Received(1).CheckAsync(
            WhisperPluginConstants.SmallModelRequiredMemoryBytes,
            WhisperPluginConstants.SmallModelRequiredDiskBytes,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_uses_large_requirements_for_other_aliases()
    {
        await _executor.ExecuteAsync(InstallOp());

        await _resourceCheck.Received(1).CheckAsync(
            WhisperPluginConstants.LargeModelRequiredMemoryBytes,
            WhisperPluginConstants.LargeModelRequiredDiskBytes,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Uninstall_disables_provider_before_stopping_container()
    {
        var calls = new List<string>();
        _providerStore.DisableAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { calls.Add("disable"); return Task.CompletedTask; });
        _applier.StopAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { calls.Add("stop"); return Task.CompletedTask; });

        var result = await _executor.ExecuteAsync(UninstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        calls.ShouldBe(["disable", "stop"]);
    }

    [Test]
    public async Task Uninstall_reports_failure_when_stop_throws()
    {
        _applier.StopAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("compose stop failed"));

        var result = await _executor.ExecuteAsync(UninstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("compose stop failed");
    }
}
