// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// State machine for the self-hosted STT engine operations (Whisper and Parakeet). Install: resolve the engine profile
/// from the model alias → capacity gate → stop the other engines → start the engine → wait for its
/// Speaches-compatible API → install the model → enable the provider row. Uninstall: disable the provider row → stop
/// every supported engine. Exactly one engine runs at a time. On install failure the engine is deliberately left
/// running (a model switch must not kill a working install). Never throws; always returns a terminal status.
/// </summary>
/// <param name="applier">Starts, stops and capability-checks the engines on this installation</param>
/// <param name="installer">Polls the engine API and installs the model</param>
/// <param name="providerStore">Maintains the well-known custom_stt_providers row</param>
/// <param name="resourceCheck">Pre-install memory and disk gate</param>
/// <param name="options">Deployment configuration (engine service names, profiles and URLs)</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Application;

public class WhisperPluginExecutor
{
    private readonly IWhisperPluginApplier _applier;
    private readonly IWhisperModelInstaller _installer;
    private readonly IWhisperProviderStore _providerStore;
    private readonly IWhisperResourceCheck _resourceCheck;
    private readonly UpdaterOptions _options;
    private readonly ILogger<WhisperPluginExecutor> _logger;

    public WhisperPluginExecutor(
        IWhisperPluginApplier applier,
        IWhisperModelInstaller installer,
        IWhisperProviderStore providerStore,
        IWhisperResourceCheck resourceCheck,
        IOptions<UpdaterOptions> options,
        ILogger<WhisperPluginExecutor> logger)
    {
        _applier = applier;
        _installer = installer;
        _providerStore = providerStore;
        _resourceCheck = resourceCheck;
        _options = options.Value;
        _logger = logger;
    }

    public Task<UpdateExecutionResult> ExecuteAsync(UpdateOperation operation, CancellationToken cancellationToken = default)
    {
        return operation.OperationType == UpdateOperationType.WhisperUninstall
            ? UninstallAsync(operation, cancellationToken)
            : InstallAsync(operation, cancellationToken);
    }

    private async Task<UpdateExecutionResult> InstallAsync(UpdateOperation operation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operation.ArtifactRef))
        {
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, "Whisper install operation has no model reference.");
        }

        var profile = SttEngineProfiles.Resolve(operation.TargetVersion, _options);
        if (profile is null)
        {
            _logger.LogWarning("Whisper install {Id} rejected: unknown model alias {Alias}.", operation.Id, operation.TargetVersion);
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, $"Unknown model alias '{operation.TargetVersion}'.");
        }

        if (!_applier.Supports(profile))
        {
            return new UpdateExecutionResult(
                UpdateExecutionStatus.Failed,
                null,
                $"The self-hosted {profile.ProviderName} is not available on this installation.");
        }

        try
        {
            var capacityError = await _resourceCheck.CheckAsync(profile.RequiredMemoryBytes, profile.RequiredDiskBytes, cancellationToken);
            if (capacityError is not null)
            {
                _logger.LogWarning("Whisper install {Id} rejected: {Reason}", operation.Id, capacityError);
                return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, capacityError);
            }

            await StopOtherEnginesAsync(profile, cancellationToken);
            await _applier.StartAsync(profile, cancellationToken);

            if (!await _installer.WaitForReadyAsync(profile.BaseUrl, cancellationToken))
            {
                return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, $"{profile.ProviderName} service did not become ready.");
            }

            await _installer.InstallModelAsync(profile.BaseUrl, operation.ArtifactRef, cancellationToken);
            await _providerStore.UpsertEnabledAsync(operation.ArtifactRef, profile.ProviderName, profile.BaseUrl, cancellationToken);

            return new UpdateExecutionResult(UpdateExecutionStatus.Succeeded, null, $"Whisper installed (model {operation.TargetVersion}).");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Whisper install {Id} failed.", operation.Id);
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, $"Whisper install failed: {ex.Message}");
        }
    }

    private async Task<UpdateExecutionResult> UninstallAsync(UpdateOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            await _providerStore.DisableAsync(cancellationToken);

            var stopErrors = new List<Exception>();
            var attempted = 0;
            foreach (var profile in SttEngineProfiles.All(_options).Where(_applier.Supports))
            {
                attempted++;
                try
                {
                    await _applier.StopAsync(profile, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Stopping STT service {Service} failed during uninstall {Id}.", profile.ServiceName, operation.Id);
                    stopErrors.Add(ex);
                }
            }

            if (attempted > 0 && stopErrors.Count == attempted)
            {
                throw stopErrors[0];
            }

            return new UpdateExecutionResult(UpdateExecutionStatus.Succeeded, null, "Whisper uninstalled.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Whisper uninstall {Id} failed.", operation.Id);
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, $"Whisper uninstall failed: {ex.Message}");
        }
    }

    private async Task StopOtherEnginesAsync(SttEngineProfile target, CancellationToken cancellationToken)
    {
        var others = SttEngineProfiles.All(_options)
            .Where(p => p.ServiceName != target.ServiceName && _applier.Supports(p));

        foreach (var other in others)
        {
            try
            {
                await _applier.StopAsync(other, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Stopping STT service {Service} before installing {Target} failed.", other.ServiceName, target.ServiceName);
            }
        }
    }
}
