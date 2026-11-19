// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Resolves the directory of the PostgreSQL client binaries of the engine that is active right now (empty: PATH).
/// Evaluated on every call, because an engine upgrade switches the engine while the updater keeps running.
/// </summary>
public interface IPostgresBinLocator
{
    string ResolveBinDirectory();
}
