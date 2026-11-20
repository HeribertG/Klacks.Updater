// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

/// <summary>
/// The effective values of the postgres service in `docker compose config --format json` that the engine upgrade needs.
/// </summary>
/// <param name="Image">Resolved image reference</param>
/// <param name="ContainerName">container_name of the service</param>
/// <param name="DataDirectory">Resolved PGDATA environment value</param>
public record ComposePostgresService(string? Image, string? ContainerName, string? DataDirectory);
