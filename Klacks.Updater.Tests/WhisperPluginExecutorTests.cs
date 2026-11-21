// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class WhisperPluginExecutorTests
{
    private const string LargeModelId = "deepdml/faster-whisper-large-v3-turbo-ct2";
    private const string SmallModelId = "Systran/faster-whisper-small";
    private const string ParakeetModelId = "parakeet-tdt-0.6b-v3-int8";

    private readonly UpdaterOptions _options = new();

    private IWhisperPluginApplier _applier = null!;
    private IWhisperModelInstaller _installer = null!;
    private IWhisperProviderStore _providerStore = null!;
    private IWhisperResourceCheck _resourceCheck = null!;
    private ILocalTtsStore _ttsStore = null!;
    private WhisperPluginExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _applier = Substitute.For<IWhisperPluginApplier>();
        _applier.Supports(Arg.Any<SttEngineProfile>()).Returns(true);
        _installer = Substitute.For<IWhisperModelInstaller>();
        _providerStore = Substitute.For<IWhisperProviderStore>();
        _resourceCheck = Substitute.For<IWhisperResourceCheck>();
        _ttsStore = Substitute.For<ILocalTtsStore>();
        _resourceCheck.CheckAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _installer.WaitForReadyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _executor = new WhisperPluginExecutor(
            _applier,
            _installer,
            _providerStore,
            _resourceCheck,
            _ttsStore,
            Options.Create(_options),
            NullLogger<WhisperPluginExecutor>.Instance);
    }

    private static UpdateOperation InstallOp(string alias = WhisperPluginConstants.ModelAliasLarge, string? modelId = LargeModelId) => new()
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
        TargetVersion = WhisperPluginConstants.ModelAliasLarge,
    };

    private static bool IsService(SttEngineProfile? profile, string serviceName) => profile?.ServiceName == serviceName;

    [Test]
    public async Task Install_succeeds_and_enables_provider()
    {
        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        await _applier.Received(1).StartAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
        await _installer.Received(1).InstallModelAsync(_options.WhisperBaseUrl, LargeModelId, Arg.Any<CancellationToken>());
        await _providerStore.Received(1).UpsertEnabledAsync(
            LargeModelId,
            WhisperPluginConstants.ProviderName,
            _options.WhisperBaseUrl,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_fails_without_model_reference()
    {
        var result = await _executor.ExecuteAsync(InstallOp(modelId: null));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.DidNotReceive().StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_fails_on_capacity_rejection_without_starting_container()
    {
        _resourceCheck.CheckAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns("Not enough free memory");

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldBe("Not enough free memory");
        await _applier.DidNotReceive().StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_fails_when_service_never_ready_and_skips_model_install()
    {
        _installer.WaitForReadyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _installer.DidNotReceive().InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _providerStore.DidNotReceive().UpsertEnabledAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_fails_when_model_install_throws_and_keeps_container_running()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("download failed"));

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("download failed");
        await _providerStore.DidNotReceive().UpsertEnabledAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().StopAsync(
            Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
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
    public async Task Install_uses_large_requirements_for_large_alias()
    {
        await _executor.ExecuteAsync(InstallOp());

        await _resourceCheck.Received(1).CheckAsync(
            WhisperPluginConstants.LargeModelRequiredMemoryBytes,
            WhisperPluginConstants.LargeModelRequiredDiskBytes,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_Parakeet_StopsWhisperAndStartsStt()
    {
        var calls = new List<string>();
        _applier.StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>())
            .Returns(call => { calls.Add($"stop:{call.Arg<SttEngineProfile>()!.ServiceName}"); return Task.CompletedTask; });
        _applier.StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>())
            .Returns(call => { calls.Add($"start:{call.Arg<SttEngineProfile>()!.ServiceName}"); return Task.CompletedTask; });

        var result = await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        calls.ShouldBe([$"start:{_options.SttServiceName}", $"stop:{_options.WhisperServiceName}"]);
        await _installer.Received(1).WaitForReadyAsync(_options.SttBaseUrl, Arg.Any<CancellationToken>());
        await _installer.Received(1).InstallModelAsync(_options.SttBaseUrl, ParakeetModelId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_TargetStartFails_OtherEngineIsNotStopped()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        _applier.StartAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("service missing"));

        var result = await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.DidNotReceive().StopAsync(
            Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
        await _providerStore.DidNotReceive().UpsertEnabledAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_ModelInstallFailsOnEngineChange_StopsNewcomerAndLeavesOtherRunning()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("download failed"));

        var result = await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.Received(1).StopAsync(
            Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), CancellationToken.None);
        await _applier.DidNotReceive().StopAsync(
            Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_NotReadyOnEngineChange_StopsNewcomerAndLeavesOtherRunning()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        _installer.WaitForReadyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.Received(1).StopAsync(
            Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), CancellationToken.None);
        await _applier.DidNotReceive().StopAsync(
            Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_RollbackStopFailure_StillReportsOriginalFailure()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("download failed"));
        _applier.StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("stop failed"));

        var result = await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("download failed");
    }

    [Test]
    public async Task Install_HttpTimeoutWithoutRequestedCancellation_FailsAndStopsNewcomer()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        var result = await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.Received(1).StopAsync(
            Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), CancellationToken.None);
        await _providerStore.DidNotReceive().UpsertEnabledAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_RequestedCancellation_StopsNewcomerAndRethrows()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        using var source = new CancellationTokenSource();
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                source.Cancel();
                return Task.FromCanceled(source.Token);
            });

        await Should.ThrowAsync<OperationCanceledException>(
            () => _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId), source.Token));

        await _applier.Received(1).StopAsync(
            Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), CancellationToken.None);
    }

    [Test]
    public async Task Install_RequestedCancellation_SameEngine_StopsNothing()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        using var source = new CancellationTokenSource();
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                source.Cancel();
                return Task.FromCanceled(source.Token);
            });

        await Should.ThrowAsync<OperationCanceledException>(
            () => _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasLarge, LargeModelId), source.Token));

        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_StopFailureOfOtherEngine_IsReportedInTheSuccessMessage()
    {
        _applier.StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("no such service"));

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        result.Message.ShouldEndWith(WhisperPluginExecutor.PreviousEngineStopFailedNote);
    }

    [Test]
    public async Task Install_StopOfOtherEngineSucceeds_HasNoWarningNote()
    {
        var result = await _executor.ExecuteAsync(InstallOp());

        result.Message.ShouldNotContain("failed");
    }

    [Test]
    public async Task Install_SameEngineModelSwitchFails_StopsNothing()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("download failed"));

        var result = await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasLarge, LargeModelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_SameEngineNotReady_StopsNothing()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        _installer.WaitForReadyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasSmall, SmallModelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_StopsOtherEnginesOnlyAfterUpsert()
    {
        await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        Received.InOrder(() =>
        {
            _applier.StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
            _installer.WaitForReadyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            _providerStore.UpsertEnabledAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            _applier.StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
        });
    }

    [TestCase(WhisperPluginConstants.ModelAliasSmall, LargeModelId)]
    [TestCase(WhisperPluginConstants.ModelAliasLarge, SmallModelId)]
    [TestCase(WhisperPluginConstants.ModelAliasParakeet, LargeModelId)]
    [TestCase(WhisperPluginConstants.ModelAliasLarge, "attacker/other-model")]
    public async Task Install_ModelReferenceNotMatchingAlias_FailsWithoutStarting(string alias, string modelId)
    {
        var result = await _executor.ExecuteAsync(InstallOp(alias, modelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain(modelId);
        await _applier.DidNotReceive().StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
        await _installer.DidNotReceive().InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_Whisper_StopsStt()
    {
        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        await _applier.Received(1).StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_StopFailureOfOtherEngine_IsNotFatal()
    {
        _applier.StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("no such service"));

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        await _applier.Received(1).StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_UnsupportedProfile_FailsWithoutStarting()
    {
        _applier.Supports(Arg.Is<SttEngineProfile>(p => p != null && !p.IsParakeet)).Returns(false);

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldBe($"{WhisperPluginConstants.ProviderName} is not available on this installation.");
        await _resourceCheck.DidNotReceive().CheckAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_UnsupportedOtherEngine_IsNotStopped()
    {
        _applier.Supports(Arg.Is<SttEngineProfile>(p => p != null && !p.IsParakeet)).Returns(false);

        var result = await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_UnknownAlias_Fails()
    {
        var result = await _executor.ExecuteAsync(InstallOp(alias: "does-not-exist"));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("Unknown model alias");
        await _applier.DidNotReceive().StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_Parakeet_UpsertsParakeetNameAndUrl()
    {
        await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        await _providerStore.Received(1).UpsertEnabledAsync(
            ParakeetModelId,
            WhisperPluginConstants.ParakeetProviderName,
            _options.SttBaseUrl,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Uninstall_disables_provider_before_stopping_container()
    {
        var calls = new List<string>();
        _providerStore.DisableAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { calls.Add("disable"); return Task.CompletedTask; });
        _applier.StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>())
            .Returns(_ => { calls.Add("stop"); return Task.CompletedTask; });

        var result = await _executor.ExecuteAsync(UninstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        calls[0].ShouldBe("disable");
        calls.Skip(1).ShouldAllBe(c => c == "stop");
    }

    [Test]
    public async Task Uninstall_StopsAllSupportedEngines()
    {
        _applier.Supports(Arg.Is<SttEngineProfile>(p => p != null && !p.IsParakeet)).Returns(false);

        var result = await _executor.ExecuteAsync(UninstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        await _applier.Received(1).StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Uninstall_StopsBothEnginesWhenBothSupported()
    {
        await _executor.ExecuteAsync(UninstallOp());

        await _applier.Received(1).StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
        await _applier.Received(1).StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Uninstall_succeeds_when_only_one_engine_stop_throws()
    {
        _applier.StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("no such service"));

        var result = await _executor.ExecuteAsync(UninstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
    }

    [Test]
    public async Task Uninstall_reports_failure_when_every_stop_throws()
    {
        _applier.StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("compose stop failed"));

        var result = await _executor.ExecuteAsync(UninstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("compose stop failed");
    }

    [Test]
    public async Task Install_Whisper_WhileLocalTtsInstalled_DoesNotStopStt()
    {
        _ttsStore.IsInstalledAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        result.Message.ShouldNotContain("failed");
        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
        await _providerStore.Received(1).UpsertEnabledAsync(
            LargeModelId, WhisperPluginConstants.ProviderName, _options.WhisperBaseUrl, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_Whisper_WithoutLocalTts_StillStopsStt()
    {
        _ttsStore.IsInstalledAsync(Arg.Any<CancellationToken>()).Returns(false);

        await _executor.ExecuteAsync(InstallOp());

        await _applier.Received(1).StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_Parakeet_WhileLocalTtsInstalled_StillStopsWhisper()
    {
        _ttsStore.IsInstalledAsync(Arg.Any<CancellationToken>()).Returns(true);

        await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        await _applier.Received(1).StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_ParakeetFailsOnEngineChange_WhileLocalTtsInstalled_DoesNotStopStt()
    {
        _ttsStore.IsInstalledAsync(Arg.Any<CancellationToken>()).Returns(true);
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.WhisperBaseUrl);
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("download failed"));

        var result = await _executor.ExecuteAsync(InstallOp(WhisperPluginConstants.ModelAliasParakeet, ParakeetModelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Uninstall_WhileLocalTtsInstalled_DoesNotStopStt()
    {
        _ttsStore.IsInstalledAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await _executor.ExecuteAsync(UninstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        await _providerStore.Received(1).DisableAsync(Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.SttServiceName)), Arg.Any<CancellationToken>());
        await _applier.Received(1).StopAsync(Arg.Is<SttEngineProfile>(p => IsService(p, _options.WhisperServiceName)), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Uninstall_WhileLocalTtsInstalled_OnWindowsLikeApplier_StopsNothing()
    {
        _ttsStore.IsInstalledAsync(Arg.Any<CancellationToken>()).Returns(true);
        _applier.Supports(Arg.Is<SttEngineProfile>(p => p != null && !p.IsParakeet)).Returns(false);

        var result = await _executor.ExecuteAsync(UninstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }
}
