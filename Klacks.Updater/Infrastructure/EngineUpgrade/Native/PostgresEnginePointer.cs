// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Native;

/// <summary>
/// Content of postgres-engine.json (schema defined together with the installer): the active PostgreSQL engine of a
/// native installation. The installer pins PostgresDirectory to &lt;ProgramRoot&gt;\pgsql-&lt;major&gt; and DataDirectory to
/// &lt;DataRoot&gt;\pgdata-&lt;major&gt; and refuses anything else.
/// </summary>
/// <param name="SchemaVersion">Pointer schema version, currently 1</param>
/// <param name="Major">Major version of the active engine</param>
/// <param name="PostgresDirectory">Engine root with bin, lib and share</param>
/// <param name="DataDirectory">Data directory of the active cluster</param>
public record PostgresEnginePointer(int SchemaVersion, int Major, string PostgresDirectory, string DataDirectory)
{
    public const int CurrentSchemaVersion = 1;

    [JsonIgnore]
    public string BinDirectory => NativeEngineLayout.BinDirectory(PostgresDirectory);
}
