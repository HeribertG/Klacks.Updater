// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

/// <summary>
/// Talks to the Speaches container: waits until its HTTP API is reachable and installs a model.
/// The explicit install call is required because Speaches does not download models on the first
/// transcription request.
/// </summary>
public interface IWhisperModelInstaller
{
    Task<bool> WaitForReadyAsync(CancellationToken cancellationToken = default);

    Task InstallModelAsync(string modelId, CancellationToken cancellationToken = default);
}
