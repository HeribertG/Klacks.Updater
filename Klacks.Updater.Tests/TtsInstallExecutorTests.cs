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
public class TtsInstallExecutorTests
{
    private readonly UpdaterOptions _options = new();

    private IWhisperPluginApplier _applier = null!;
    private IWhisperModelInstaller _installer = null!;
    private ILocalTtsStore _ttsStore = null!;
    private IWhisperProviderStore _providerStore = null!;
    private TtsInstallExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _applier = Substitute.For<IWhisperPluginApplier>();
        _applier.Supports(Arg.Any<SttEngineProfile>()).Returns(true);
        _installer = Substitute.For<IWhisperModelInstaller>();
        _installer.WaitForReadyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _ttsStore = Substitute.For<ILocalTtsStore>();
        _providerStore = Substitute.For<IWhisperProviderStore>();
        _executor = new TtsInstallExecutor(
            _applier,
            _installer,
            _ttsStore,
            _providerStore,
            Options.Create(_options),
            NullLogger<TtsInstallExecutor>.Instance);
    }

    private static UpdateOperation InstallOp(
        string alias = TtsPluginConstants.ModelAlias,
        string? modelId = TtsPluginConstants.ModelId) => new()
    {
        Id = Guid.NewGuid(),
        OperationType = UpdateOperationType.TtsInstall,
        TargetVersion = alias,
        ArtifactRef = modelId,
    };

    private static bool IsStt(SttEngineProfile? profile, UpdaterOptions options) => profile?.ServiceName == options.SttServiceName;

    [Test]
    public async Task Install_starts_stt_waits_installs_model_and_writes_the_base_url_in_order()
    {
        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        result.Message.ShouldBe(TtsPluginConstants.SuccessMessage);
        Received.InOrder(() =>
        {
            _applier.StartAsync(Arg.Is<SttEngineProfile>(p => IsStt(p, _options)), Arg.Any<CancellationToken>());
            _installer.WaitForReadyAsync(_options.SttBaseUrl, Arg.Any<CancellationToken>());
            _installer.InstallModelAsync(_options.SttBaseUrl, TtsPluginConstants.ModelId, Arg.Any<CancellationToken>());
            _ttsStore.UpsertBaseUrlAsync(_options.SttBaseUrl, Arg.Any<CancellationToken>());
        });
        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_writes_the_configured_native_base_url()
    {
        _options.SttBaseUrl = "http://localhost:5091";

        await _executor.ExecuteAsync(InstallOp());

        await _ttsStore.Received(1).UpsertBaseUrlAsync("http://localhost:5091", Arg.Any<CancellationToken>());
        await _installer.Received(1).InstallModelAsync("http://localhost:5091", TtsPluginConstants.ModelId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_uses_the_parakeet_compose_profile_and_service()
    {
        await _executor.ExecuteAsync(InstallOp());

        await _applier.Received(1).StartAsync(
            Arg.Is<SttEngineProfile>(p => p.ServiceName == _options.SttServiceName && p.ComposeProfile == _options.SttComposeProfile),
            Arg.Any<CancellationToken>());
    }

    [TestCase("supertonic-3", "other-model")]
    [TestCase("other-alias", TtsPluginConstants.ModelId)]
    [TestCase("supertonic-3", null)]
    public async Task Install_with_a_wrong_alias_or_model_reference_fails_without_any_change(string alias, string? modelId)
    {
        var result = await _executor.ExecuteAsync(InstallOp(alias, modelId));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.DidNotReceive().StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
        await _ttsStore.DidNotReceive().UpsertBaseUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_on_an_installation_without_stt_support_fails_without_starting()
    {
        _applier.Supports(Arg.Any<SttEngineProfile>()).Returns(false);

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldBe(TtsPluginConstants.UnsupportedMessage);
        await _applier.DidNotReceive().StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_model_404_fails_with_the_too_old_message_and_writes_no_setting()
    {
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SttModelNotFoundException("Model install failed with 404"));

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldBe("Klacks.Stt version too old for local speech output.");
        await _ttsStore.DidNotReceive().UpsertBaseUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_model_failure_reports_the_reason_and_writes_no_setting()
    {
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Model install failed with 502: upstream"));

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("502");
        await _ttsStore.DidNotReceive().UpsertBaseUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_service_never_ready_fails_without_model_install_or_setting()
    {
        _installer.WaitForReadyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldBe(TtsPluginConstants.ServiceNotReadyMessage);
        await _installer.DidNotReceive().InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _ttsStore.DidNotReceive().UpsertBaseUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_start_failure_fails_and_writes_no_setting()
    {
        _applier.StartAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("service missing"));

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("service missing");
        await _ttsStore.DidNotReceive().UpsertBaseUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_setting_write_failure_is_a_failed_install()
    {
        _ttsStore.UpsertBaseUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("db down");
    }

    [Test]
    public async Task Failed_install_stops_stt_again_when_nothing_else_needs_it()
    {
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("download failed"));

        await _executor.ExecuteAsync(InstallOp());

        await _applier.Received(1).StopAsync(Arg.Is<SttEngineProfile>(p => IsStt(p, _options)), CancellationToken.None);
    }

    [Test]
    public async Task Failed_install_keeps_stt_running_while_parakeet_is_the_enabled_engine()
    {
        _providerStore.GetEnabledApiUrlAsync(Arg.Any<CancellationToken>()).Returns(_options.SttBaseUrl);
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("download failed"));

        await _executor.ExecuteAsync(InstallOp());

        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Failed_reinstall_keeps_stt_running_while_local_tts_is_already_installed()
    {
        _ttsStore.IsInstalledAsync(Arg.Any<CancellationToken>()).Returns(true);
        _installer.WaitForReadyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _applier.DidNotReceive().StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Failed_install_reports_the_original_error_when_the_rollback_stop_throws()
    {
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("download failed"));
        _applier.StopAsync(Arg.Any<SttEngineProfile>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("stop failed"));

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("download failed");
    }

    [Test]
    public async Task Install_http_timeout_without_requested_cancellation_is_a_plain_failure()
    {
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        var result = await _executor.ExecuteAsync(InstallOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _ttsStore.DidNotReceive().UpsertBaseUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Install_requested_cancellation_rolls_back_and_rethrows()
    {
        using var source = new CancellationTokenSource();
        _installer.InstallModelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                source.Cancel();
                return Task.FromCanceled(source.Token);
            });

        await Should.ThrowAsync<OperationCanceledException>(() => _executor.ExecuteAsync(InstallOp(), source.Token));

        await _applier.Received(1).StopAsync(Arg.Is<SttEngineProfile>(p => IsStt(p, _options)), CancellationToken.None);
        await _ttsStore.DidNotReceive().UpsertBaseUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
