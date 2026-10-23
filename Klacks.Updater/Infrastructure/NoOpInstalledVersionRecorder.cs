// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Version recorder for platforms without a setup-program entry to keep in sync (macOS): records nothing.
/// </summary>

using Klacks.Updater.Domain;

namespace Klacks.Updater.Infrastructure;

public class NoOpInstalledVersionRecorder : IInstalledVersionRecorder
{
    public void Record(string version)
    {
    }
}
