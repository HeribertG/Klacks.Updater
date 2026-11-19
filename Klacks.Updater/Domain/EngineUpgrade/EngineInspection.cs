// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

/// <summary>
/// Read-only view of the running engine before an upgrade starts.
/// </summary>
/// <param name="CurrentMajor">Major version of the running cluster as the target sees it</param>
/// <param name="OldState">Target-specific facts about the old engine (paths, image, service binPath) that a revert needs</param>
/// <param name="FreeBytes">Free space where dumps and the new cluster will be written</param>
/// <param name="UnsupportedReason">Non-null when this deployment cannot run an engine upgrade; nothing may be changed then</param>
public record EngineInspection(int CurrentMajor, IReadOnlyDictionary<string, string> OldState, long FreeBytes, string? UnsupportedReason);
