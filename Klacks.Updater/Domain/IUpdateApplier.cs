// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Target-specific application of an update (Docker / native). Activating stops the current app and starts the
/// operation's artifact: Docker pulls the image tag first, the native applier downloads and hash-checks the bundle.
/// ActivateVersionAsync switches back to a version that is already present (rollback).
/// </summary>
public interface IUpdateApplier
{
    Task ActivateAsync(UpdateOperation operation, CancellationToken cancellationToken = default);

    Task ActivateVersionAsync(string version, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
