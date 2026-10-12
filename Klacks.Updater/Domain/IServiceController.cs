// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Stops and starts the natively installed Klacks application service (Windows service, launchd daemon).
/// Both calls return only once the service has reached the requested state.
/// </summary>
public interface IServiceController
{
    Task StopAsync(CancellationToken cancellationToken = default);

    Task StartAsync(CancellationToken cancellationToken = default);
}
