// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

/// <summary>
/// Pre-install capacity gate: verifies the host has enough free memory and disk for the selected
/// Whisper model. Returns null when capacity suffices, otherwise an English rejection message.
/// </summary>
public interface IWhisperResourceCheck
{
    Task<string?> CheckAsync(long requiredMemoryBytes, long requiredDiskBytes, CancellationToken cancellationToken = default);
}
