// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// One self-hosted speech-recognition engine the updater can install: the model alias that selects it, the compose
/// service/profile (or native service) that runs it, the base URL of its Speaches-compatible API, the provider row name
/// and the capacity it needs.
/// </summary>
public sealed record SttEngineProfile(
    string Alias,
    bool IsParakeet,
    string ServiceName,
    string ComposeProfile,
    string BaseUrl,
    string ProviderName,
    long RequiredMemoryBytes,
    long RequiredDiskBytes);
