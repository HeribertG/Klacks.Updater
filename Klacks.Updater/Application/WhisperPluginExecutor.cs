// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// State machine for Whisper plugin operations. Install: capacity gate → start container → wait for
/// the Speaches API → install the model → enable the provider row. Uninstall: disable the provider
/// row → stop the container. On install failure the container is deliberately left running when a
/// provider row is already enabled (model switch must not kill the working install). Never throws;
/// always returns a terminal status.
/// </summary>
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;

namespace Klacks.Updater.Application;

public class WhisperPluginExecutor
{
    private readonly IWhisperPluginApplier _applier;
    private readonly IWhisperModelInstaller _installer;
    private readonly IWhisperProviderStore _providerStore;
    private readonly IWhisperResourceCheck _resourceCheck;
    private readonly ILogger<WhisperPluginExecutor> _logger;

    public WhisperPluginExecutor(
        IWhisperPluginApplier applier,
        IWhisperModelInstaller installer,
        IWhisperProviderStore providerStore,
        IWhisperResourceCheck resourceCheck,
        ILogger<WhisperPluginExecutor> logger)
    {
        _applier = applier;
        _installer = installer;
        _providerStore = providerStore;
        _resourceCheck = resourceCheck;
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

        try
        {
            var (requiredMemory, requiredDisk) = ResolveRequirements(operation.TargetVersion);
            var capacityError = await _resourceCheck.CheckAsync(requiredMemory, requiredDisk, cancellationToken);
            if (capacityError is not null)
            {
                _logger.LogWarning("Whisper install {Id} rejected: {Reason}", operation.Id, capacityError);
                return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, capacityError);
            }

            await _applier.StartAsync(cancellationToken);

            if (!await _installer.WaitForReadyAsync(cancellationToken))
            {
                return new UpdateExecutionResult(UpdateExecutionStatus.Failed, null, "Whisper service did not become ready.");
            }

            await _installer.InstallModelAsync(operation.ArtifactRef, cancellationToken);
            await _providerStore.UpsertEnabledAsync(operation.ArtifactRef, cancellationToken);

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
            await _applier.StopAsync(cancellationToken);
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

    private static (long RequiredMemoryBytes, long RequiredDiskBytes) ResolveRequirements(string modelAlias)
    {
        return modelAlias == WhisperPluginConstants.ModelAliasSmall
            ? (WhisperPluginConstants.SmallModelRequiredMemoryBytes, WhisperPluginConstants.SmallModelRequiredDiskBytes)
            : (WhisperPluginConstants.LargeModelRequiredMemoryBytes, WhisperPluginConstants.LargeModelRequiredDiskBytes);
    }
}
