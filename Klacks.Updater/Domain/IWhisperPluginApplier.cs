// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

/// <summary>
/// Starts and stops the self-hosted Whisper STT compose service (profile-gated so a plain
/// docker compose up never starts it on installations that did not opt in).
/// </summary>
public interface IWhisperPluginApplier
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
