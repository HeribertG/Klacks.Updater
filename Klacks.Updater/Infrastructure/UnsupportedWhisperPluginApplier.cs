// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Whisper STT ships only as a Docker compose service. Native installations register this applier so a Whisper
/// install request fails with a clear reason instead of a missing docker binary.
/// </summary>

using Klacks.Updater.Domain;

namespace Klacks.Updater.Infrastructure;

public class UnsupportedWhisperPluginApplier : IWhisperPluginApplier
{
    private const string Reason = "The self-hosted Whisper service is only available for Docker installations.";

    public Task StartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException(Reason);

    public Task StopAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException(Reason);
}
