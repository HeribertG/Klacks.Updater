// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// State machine for the self-hosted STT engine operations (Whisper and Parakeet). Install: resolve the engine profile
/// from the model alias → verify the model reference → capacity gate → start the engine → wait for its
/// Speaches-compatible API → install the model → enable the provider row → only then stop the other engines (a failed
/// switch must never leave the system without a working engine). A failure after the start of an engine that was not
/// yet the enabled one stops that newcomer again; a model switch of the running engine leaves it running. Uninstall:
/// disable the provider row → stop every supported engine. Exactly one engine runs at a time. Returns a terminal status
/// and never throws, except that a cancellation requested through the token (updater shutdown) is rethrown after a
/// best-effort stop of a just-started newcomer; a timeout that is not a requested cancellation is a plain failure.
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
    public const string PreviousEngineStopFailedNote = " (stopping the previous engine failed; see updater log)";

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
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, "Speech engine install operation has no model reference.");
        }

        var profile = SttEngineProfiles.Resolve(operation.TargetVersion, _options);
        if (profile is null)
        {
            _logger.LogWarning("STT install {Id} rejected: unknown model alias {Alias}.", operation.Id, operation.TargetVersion);
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, $"Unknown model alias '{operation.TargetVersion}'.");
        }

        if (!string.Equals(operation.ArtifactRef, profile.ModelId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "STT install {Id} rejected: model reference {ModelRef} does not match alias {Alias}.",
                operation.Id,
                operation.ArtifactRef,
                operation.TargetVersion);
            return new UpdateExecutionResult(
                UpdateExecutionStatus.Failed,
                null,
                $"Model reference '{operation.ArtifactRef}' does not match model alias '{operation.TargetVersion}'.");
        }

        if (!_applier.Supports(profile))
        {
            return new UpdateExecutionResult(
                UpdateExecutionStatus.Failed,
                null,
                $"{profile.ProviderName} is not available on this installation.");
        }

        var newcomerStartAttempted = false;
        try
        {
            var capacityError = await _resourceCheck.CheckAsync(profile.RequiredMemoryBytes, profile.RequiredDiskBytes, cancellationToken);
            if (capacityError is not null)
            {
                _logger.LogWarning("STT install {Id} rejected: {Reason}", operation.Id, capacityError);
                return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, capacityError);
            }

            var previousApiUrl = await _providerStore.GetEnabledApiUrlAsync(cancellationToken);
            var isNewcomer = !string.Equals(previousApiUrl, profile.BaseUrl, StringComparison.Ordinal);

            newcomerStartAttempted = isNewcomer;
            await _applier.StartAsync(profile, cancellationToken);

            if (!await _installer.WaitForReadyAsync(profile.BaseUrl, cancellationToken))
            {
                await StopNewcomerAsync(profile, newcomerStartAttempted);
                return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, $"{profile.ProviderName} service did not become ready.");
            }

            await _installer.InstallModelAsync(profile.BaseUrl, operation.ArtifactRef, cancellationToken);
            await _providerStore.UpsertEnabledAsync(operation.ArtifactRef, profile.ProviderName, profile.BaseUrl, cancellationToken);
            var previousEnginesStopped = await StopOtherEnginesAsync(profile, cancellationToken);

            var message = $"{profile.ProviderName} installed (model {operation.TargetVersion}).";
            return new UpdateExecutionResult(
                UpdateExecutionStatus.Succeeded,
                null,
                previousEnginesStopped ? message : message + PreviousEngineStopFailedNote);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await StopNewcomerAsync(profile, newcomerStartAttempted);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "STT install {Id} failed.", operation.Id);
            await StopNewcomerAsync(profile, newcomerStartAttempted);
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, $"{profile.ProviderName} install failed: {ex.Message}");
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
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Stopping STT service {Service} failed during uninstall {Id}.", profile.ServiceName, operation.Id);
                    stopErrors.Add(ex);
                }
            }

            if (attempted > 0 && stopErrors.Count == attempted)
            {
                throw stopErrors[0];
            }

            return new UpdateExecutionResult(UpdateExecutionStatus.Succeeded, null, "Speech engine uninstalled.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "STT uninstall {Id} failed.", operation.Id);
            return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, $"Speech engine uninstall failed: {ex.Message}");
        }
    }

    private async Task<bool> StopOtherEnginesAsync(SttEngineProfile target, CancellationToken cancellationToken)
    {
        var allStopped = true;
        var others = SttEngineProfiles.All(_options)
            .Where(p => p.ServiceName != target.ServiceName && _applier.Supports(p));

        foreach (var other in others)
        {
            try
            {
                await _applier.StopAsync(other, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                allStopped = false;
                _logger.LogWarning(ex, "Could not stop STT service {Service} after installing {Target}.", other.ServiceName, target.ServiceName);
            }
        }

        return allStopped;
    }

    private async Task StopNewcomerAsync(SttEngineProfile profile, bool newcomerStartAttempted)
    {
        if (!newcomerStartAttempted)
        {
            return;
        }

        try
        {
            await _applier.StopAsync(profile, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not stop STT service {Service} while rolling back a failed install.", profile.ServiceName);
        }
    }
}
