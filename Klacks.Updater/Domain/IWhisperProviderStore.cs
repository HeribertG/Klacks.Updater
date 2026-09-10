// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

/// <summary>
/// Maintains the well-known Whisper row in the custom_stt_providers table so the API lists the
/// provider (and its model) exactly when the container is actually installed.
/// </summary>
public interface IWhisperProviderStore
{
    Task UpsertEnabledAsync(string modelId, CancellationToken cancellationToken = default);

    Task DisableAsync(CancellationToken cancellationToken = default);
}
