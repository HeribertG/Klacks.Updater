// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Makes sure the native speech recognition service is registered with the operating system, registering it when an
/// installation that predates the service lacks it. Throws when the installed Klacks version ships no such service.
/// </summary>
public interface ISttServiceRegistrar
{
    Task EnsureRegisteredAsync(CancellationToken cancellationToken = default);
}
