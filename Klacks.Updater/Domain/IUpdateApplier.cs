// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

/// <summary>
/// Target-specific application of an update (Docker / on-prem). Activating an image stops the current
/// app container and starts the target one; pulling is always done first so a rollback target that is
/// not present locally is fetched from the registry.
/// </summary>
public interface IUpdateApplier
{
    Task ActivateAsync(string imageRef, CancellationToken cancellationToken = default);

    Task ActivateVersionAsync(string version, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
