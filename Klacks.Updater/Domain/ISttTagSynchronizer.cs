// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Keeps the on-prem Docker speech service (klacks-stt) at the version pinned by the activated API image. Neither call
/// throws for a speech-service problem: the API update has already succeeded and speech recognition is secondary.
/// PlanAsync returns null when the image pins no version.
/// </summary>
public interface ISttTagSynchronizer
{
    Task<SttTagUpdate?> PlanAsync(string apiTag, CancellationToken cancellationToken = default);

    Task ApplyAsync(SttTagUpdate update, IDictionary<string, string> environment, CancellationToken cancellationToken = default);
}
