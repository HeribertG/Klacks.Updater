// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Polls the internal deep health endpoint until healthy or the timeout elapses. The timeout must
/// exceed the slowest real migration so the gate never declares failure mid-migration. Healthy also requires the
/// running API to report the expected version, so an old process that is still answering never passes the gate.
/// </summary>
public interface IUpdateHealthGate
{
    Task<bool> WaitForHealthyAsync(string expectedVersion, CancellationToken cancellationToken = default);
}
