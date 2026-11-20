// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

/// <summary>
/// Reads one service out of the JSON printed by `docker compose config --format json`. The output carries resolved
/// secrets (POSTGRES_PASSWORD), so it is parsed in memory only and never logged; only image, container name and PGDATA
/// are taken from it.
/// </summary>
public static class ComposeConfigReader
{
    private const string ServicesProperty = "services";
    private const string ImageProperty = "image";
    private const string ContainerNameProperty = "container_name";
    private const string EnvironmentProperty = "environment";
    private const string DataDirectoryVariable = "PGDATA";

    public static ComposePostgresService? ReadService(string json, string serviceName)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(ServicesProperty, out var services)
            || services.ValueKind != JsonValueKind.Object
            || !services.TryGetProperty(serviceName, out var service)
            || service.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? dataDirectory = null;
        if (service.TryGetProperty(EnvironmentProperty, out var environment) && environment.ValueKind == JsonValueKind.Object)
        {
            dataDirectory = StringOf(environment, DataDirectoryVariable);
        }

        return new ComposePostgresService(StringOf(service, ImageProperty), StringOf(service, ContainerNameProperty), dataDirectory);
    }

    private static string? StringOf(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
