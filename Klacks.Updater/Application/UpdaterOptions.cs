// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Application;

/// <summary>
/// Deployment configuration for the updater (appsettings/env). The image repository + tag env var
/// drive the Docker applier; the trust root (manifest URL, signature key) and DB connection are
/// supplied the same way. Nothing about the update source is hard-coded.
/// </summary>
public class UpdaterOptions
{
    public const string SectionName = "Updater";

    public string ConnectionString { get; set; } = string.Empty;

    public string SignaturePublicKey { get; set; } = string.Empty;

    public string HealthUrl { get; set; } = "http://klacks-api:5000/internal/health/deep";

    public string ComposeProjectDir { get; set; } = "/root/apps";

    public string ApiImageRepository { get; set; } = "ghcr.io/heribertg/klacks-api";

    public string ApiServiceName { get; set; } = "klacks-api";

    public string ApiTagEnvVar { get; set; } = "KLACKS_API_TAG";

    public string PostgresContainer { get; set; } = "klacks-postgres";

    public string PostgresUser { get; set; } = "admin";

    public string PostgresDatabase { get; set; } = "Klacks";

    public string BackupDirectory { get; set; } = "/backups";
}
