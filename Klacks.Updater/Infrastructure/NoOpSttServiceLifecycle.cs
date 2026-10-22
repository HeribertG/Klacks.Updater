// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Lifecycle for platforms without a native speech recognition service (macOS): nothing to pause or resume.
/// </summary>

using Klacks.Updater.Domain;

namespace Klacks.Updater.Infrastructure;

public class NoOpSttServiceLifecycle : ISttServiceLifecycle
{
    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StartIfAutomaticAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
