// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Starts and stops a self-hosted STT engine (Whisper or Parakeet). Docker installations run each engine as a
/// profile-gated compose service so a plain docker compose up never starts it on installations that did not opt in;
/// Supports tells the executor whether this installation can host the given engine at all.
/// </summary>
public interface IWhisperPluginApplier
{
    bool Supports(SttEngineProfile profile);

    Task StartAsync(SttEngineProfile profile, CancellationToken cancellationToken = default);

    Task StopAsync(SttEngineProfile profile, CancellationToken cancellationToken = default);
}
