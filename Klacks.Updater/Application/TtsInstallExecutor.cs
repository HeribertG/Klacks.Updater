// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// State machine for installing local text-to-speech (Supertonic 3 inside Klacks.Stt): verify alias and model reference
/// → make sure klacks-stt runs → wait for its API → install the speech model → only then write the
/// ASSISTANT_TTS_LOCAL_BASE_URL setting. A failure never writes the setting; a klacks-stt that this run started is
/// stopped again unless Parakeet speech recognition or an earlier local speech install still needs it. A 404 from the
/// model install means the running Klacks.Stt is too old. Returns a terminal status and never throws, except that a
/// cancellation requested through the token (updater shutdown) is rethrown after the best-effort rollback.
/// </summary>
/// <param name="applier">Starts and stops the klacks-stt service on this installation</param>
/// <param name="installer">Polls the Klacks.Stt API and installs the speech model</param>
/// <param name="ttsStore">Writes the setting that marks local speech output as installed</param>
/// <param name="providerStore">Tells whether Parakeet is the enabled speech recognition engine</param>
/// <param name="options">Deployment configuration (klacks-stt service name, profile and URL)</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Application;

public class TtsInstallExecutor
{
    private readonly IWhisperPluginApplier _applier;
    private readonly IWhisperModelInstaller _installer;
    private readonly ILocalTtsStore _ttsStore;
    private readonly IWhisperProviderStore _providerStore;
    private readonly UpdaterOptions _options;
    private readonly ILogger<TtsInstallExecutor> _logger;

    public TtsInstallExecutor(
        IWhisperPluginApplier applier,
        IWhisperModelInstaller installer,
        ILocalTtsStore ttsStore,
        IWhisperProviderStore providerStore,
        IOptions<UpdaterOptions> options,
        ILogger<TtsInstallExecutor> logger)
    {
        _applier = applier;
        _installer = installer;
        _ttsStore = ttsStore;
        _providerStore = providerStore;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UpdateExecutionResult> ExecuteAsync(UpdateOperation operation, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(operation.TargetVersion, TtsPluginConstants.ModelAlias, StringComparison.Ordinal)
            || !string.Equals(operation.ArtifactRef, TtsPluginConstants.ModelId, StringComparison.Ordinal))
        {
            _logger.LogWarning("TTS install {Id} rejected: unexpected alias {Alias} or model reference {ModelRef}.", operation.Id, operation.TargetVersion, operation.ArtifactRef);
            return new UpdateExecutionResult(
                UpdateExecutionStatus.Failed,
                null,
                $"Unknown local speech model '{operation.TargetVersion}' / '{operation.ArtifactRef}'.");
        }

        var profile = SttEngineProfiles.Resolve(WhisperPluginConstants.ModelAliasParakeet, _options)!;
        if (!_applier.Supports(profile))
        {
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, TtsPluginConstants.UnsupportedMessage);
        }

        var stopServiceOnFailure = false;
        try
        {
            var enabledApiUrl = await _providerStore.GetEnabledApiUrlAsync(cancellationToken);
            var parakeetEnabled = string.Equals(enabledApiUrl, profile.BaseUrl, StringComparison.Ordinal);
            stopServiceOnFailure = !parakeetEnabled && !await _ttsStore.IsInstalledAsync(cancellationToken);

            await _applier.StartAsync(profile, cancellationToken);

            if (!await _installer.WaitForReadyAsync(profile.BaseUrl, cancellationToken))
            {
                await RollbackAsync(profile, stopServiceOnFailure);
                return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, TtsPluginConstants.ServiceNotReadyMessage);
            }

            await _installer.InstallModelAsync(profile.BaseUrl, TtsPluginConstants.ModelId, cancellationToken);
            await _ttsStore.UpsertBaseUrlAsync(profile.BaseUrl, cancellationToken);

            return new UpdateExecutionResult(UpdateExecutionStatus.Succeeded, null, TtsPluginConstants.SuccessMessage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAsync(profile, stopServiceOnFailure);
            throw;
        }
        catch (SttModelNotFoundException ex)
        {
            _logger.LogError(ex, "TTS install {Id} failed: the Klacks.Stt service does not know model {Model}.", operation.Id, TtsPluginConstants.ModelId);
            await RollbackAsync(profile, stopServiceOnFailure);
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, TtsPluginConstants.ServiceTooOldMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS install {Id} failed.", operation.Id);
            await RollbackAsync(profile, stopServiceOnFailure);
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, TtsPluginConstants.InstallFailedPrefix + ex.Message);
        }
    }

    private async Task RollbackAsync(SttEngineProfile profile, bool stopService)
    {
        if (!stopService)
        {
            return;
        }

        try
        {
            await _applier.StopAsync(profile, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not stop STT service {Service} while rolling back a failed local speech install.", profile.ServiceName);
        }
    }
}
