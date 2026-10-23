// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Records the version a native installation now runs in the operating system's own installation registry (the
/// "Apps &amp; Features" entry written by the setup program), so it does not keep showing the setup version after
/// self-updates. Only an existing entry is updated; an installation without one (ZIP package) is left alone.
/// </summary>
public interface IInstalledVersionRecorder
{
    void Record(string version);
}
