// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

/// <summary>
/// Facts about the running cluster that the prechecks of an engine upgrade need.
/// </summary>
/// <param name="ServerMajor">Major version from server_version_num</param>
/// <param name="Databases">All non-template databases except the empty maintenance database "postgres"</param>
/// <param name="ForeignRoles">Roles other than the bootstrap superuser and the predefined pg_* roles</param>
/// <param name="TotalDatabaseBytes">Sum of the sizes of the migrated databases</param>
public record EngineClusterFacts(int ServerMajor, IReadOnlyList<EngineDatabase> Databases, IReadOnlyList<string> ForeignRoles, long TotalDatabaseBytes);
