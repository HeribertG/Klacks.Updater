// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Maintains the well-known self-hosted STT row in the custom_stt_providers table so the API lists the
/// provider (and its model) exactly when the engine is actually installed.
/// </summary>
public interface IWhisperProviderStore
{
    Task UpsertEnabledAsync(string modelId, string providerName, string apiUrl, CancellationToken cancellationToken = default);

    Task DisableAsync(CancellationToken cancellationToken = default);
}
