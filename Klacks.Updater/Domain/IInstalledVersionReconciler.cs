// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Brings the operating system's installation entry in line with the version the installation actually runs, once
/// when the updater starts. This catches updates that were applied by an older updater process which could not record
/// the version itself. Never throws; failures are only logged.
/// </summary>
public interface IInstalledVersionReconciler
{
    void Reconcile();
}
