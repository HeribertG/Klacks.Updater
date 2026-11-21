// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Reads and writes the settings row that marks local text-to-speech as installed. UpsertBaseUrlAsync records the base URL
/// of the Klacks.Stt service once the speech model is in place; IsInstalledAsync tells the speech-to-text executor that
/// klacks-stt must keep running because local speech output depends on it.
/// </summary>
public interface ILocalTtsStore
{
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);

    Task UpsertBaseUrlAsync(string baseUrl, CancellationToken cancellationToken = default);
}
