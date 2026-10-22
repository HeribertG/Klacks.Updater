// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Applier for engines an installation cannot host (Whisper on native installations, every engine on macOS). Supports
/// returns false so the executor fails the request with a clear reason instead of a missing docker binary; Start and
/// Stop throw when called anyway.
/// </summary>

using Klacks.Updater.Application;
using Klacks.Updater.Domain;

namespace Klacks.Updater.Infrastructure;

public class UnsupportedWhisperPluginApplier : IWhisperPluginApplier
{
    private const string Reason = "The self-hosted speech recognition is not available on this installation.";

    public bool Supports(SttEngineProfile profile) => false;

    public Task StartAsync(SttEngineProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException(Reason);

    public Task StopAsync(SttEngineProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException(Reason);
}
