// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Maps a model alias from the install operation to its engine profile (Whisper small/large via Speaches, Parakeet via
/// Klacks.Stt) using the configured service names and URLs. Exactly one engine runs at a time, so All lists every
/// engine that has to be stopped when another one is installed.
/// </summary>
namespace Klacks.Updater.Application;

public static class SttEngineProfiles
{
    public static SttEngineProfile? Resolve(string alias, UpdaterOptions options)
    {
        return alias switch
        {
            WhisperPluginConstants.ModelAliasSmall => Whisper(
                alias,
                options,
                WhisperPluginConstants.SmallModelRequiredMemoryBytes,
                WhisperPluginConstants.SmallModelRequiredDiskBytes),
            WhisperPluginConstants.ModelAliasLarge => Whisper(
                alias,
                options,
                WhisperPluginConstants.LargeModelRequiredMemoryBytes,
                WhisperPluginConstants.LargeModelRequiredDiskBytes),
            WhisperPluginConstants.ModelAliasParakeet => Parakeet(options),
            _ => null,
        };
    }

    public static IReadOnlyList<SttEngineProfile> All(UpdaterOptions options)
    {
        return
        [
            Whisper(
                WhisperPluginConstants.ModelAliasLarge,
                options,
                WhisperPluginConstants.LargeModelRequiredMemoryBytes,
                WhisperPluginConstants.LargeModelRequiredDiskBytes),
            Parakeet(options),
        ];
    }

    private static SttEngineProfile Whisper(string alias, UpdaterOptions options, long requiredMemoryBytes, long requiredDiskBytes)
    {
        return new SttEngineProfile(
            alias,
            IsParakeet: false,
            options.WhisperServiceName,
            options.WhisperComposeProfile,
            options.WhisperBaseUrl,
            WhisperPluginConstants.ProviderName,
            requiredMemoryBytes,
            requiredDiskBytes);
    }

    private static SttEngineProfile Parakeet(UpdaterOptions options)
    {
        return new SttEngineProfile(
            WhisperPluginConstants.ModelAliasParakeet,
            IsParakeet: true,
            options.SttServiceName,
            options.SttComposeProfile,
            options.SttBaseUrl,
            WhisperPluginConstants.ParakeetProviderName,
            WhisperPluginConstants.ParakeetRequiredMemoryBytes,
            WhisperPluginConstants.ParakeetRequiredDiskBytes);
    }
}
