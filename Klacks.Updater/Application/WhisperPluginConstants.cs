// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Contract constants for the Whisper plugin shared with Klacks.Api via the database: the well-known
/// provider row id/name (deploy/whisper-stt-provider.sql), the model alias the API writes into
/// target_version, and the capacity requirements per model.
/// </summary>
namespace Klacks.Updater.Application;

public static class WhisperPluginConstants
{
    public static readonly Guid ProviderId = Guid.Parse("7f3d2a10-0000-4000-8000-57705731a001");

    public const string ProviderName = "Whisper (self-hosted)";

    public const string ProviderApiUrl = "http://whisper-stt:8000";

    public const string ConnectionTypeRest = "rest";

    public const string ModelAliasSmall = "small";

    public const string ModelsPath = "/v1/models";

    public const long SmallModelRequiredMemoryBytes = 1L * 1024 * 1024 * 1024;

    public const long SmallModelRequiredDiskBytes = 1_500L * 1024 * 1024;

    public const long LargeModelRequiredMemoryBytes = 2L * 1024 * 1024 * 1024;

    public const long LargeModelRequiredDiskBytes = 3L * 1024 * 1024 * 1024;
}
