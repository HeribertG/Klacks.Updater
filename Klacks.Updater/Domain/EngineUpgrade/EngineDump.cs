// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

/// <summary>
/// A custom-format dump of one database.
/// </summary>
/// <param name="Database">Name of the dumped database</param>
/// <param name="Reference">Target-specific location of the dump file</param>
public record EngineDump(string Database, string Reference);
