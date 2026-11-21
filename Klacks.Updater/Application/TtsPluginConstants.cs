// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Contract constants for the local text-to-speech install shared with Klacks.Api via the database: the model alias the
/// API writes into target_version, the model id it writes into artifact_ref, the settings key the updater writes after a
/// successful install (its presence marks local speech output as installed) and the failure messages.
/// </summary>
namespace Klacks.Updater.Application;

public static class TtsPluginConstants
{
    public const string ModelAlias = "supertonic-3";

    public const string ModelId = "supertonic-3-int8";

    public const string LocalBaseUrlSettingKey = "ASSISTANT_TTS_LOCAL_BASE_URL";

    public const string SuccessMessage = "Local speech output installed (model " + ModelId + ").";

    public const string UnsupportedMessage = "Local speech output is not available on this installation.";

    public const string ServiceNotReadyMessage = "Klacks.Stt service did not become ready.";

    public const string ServiceTooOldMessage = "Klacks.Stt version too old for local speech output.";

    public const string InstallFailedPrefix = "Local speech output install failed: ";
}
