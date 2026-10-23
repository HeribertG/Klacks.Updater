// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Reconciler for Docker mode: the images carry their version, there is no operating-system installation entry.
/// </summary>

using Klacks.Updater.Domain;

namespace Klacks.Updater.Infrastructure;

public class NoOpInstalledVersionReconciler : IInstalledVersionReconciler
{
    public void Reconcile()
    {
    }
}
