// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Pauses and resumes the native speech recognition side service around a Klacks version switch, so it never keeps
/// running binaries of a version directory that is about to be replaced or pruned. Both calls are no-ops when the
/// service is not installed; resuming only restarts a service whose start type is automatic.
/// </summary>
public interface ISttServiceLifecycle
{
    Task StopAsync(CancellationToken cancellationToken = default);

    Task StartIfAutomaticAsync(CancellationToken cancellationToken = default);
}
