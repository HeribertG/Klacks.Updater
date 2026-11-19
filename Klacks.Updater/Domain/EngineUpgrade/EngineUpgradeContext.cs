// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

/// <summary>
/// Mutable working state of one engine upgrade, journaled after every phase. Targets record what they need for a
/// revert in OldState and what they created in NewState; values must never contain secrets.
/// </summary>
/// <param name="OperationId">update_history row id</param>
/// <param name="FromMajor">Source major (from_version)</param>
/// <param name="ToMajor">Target major (target_version)</param>
/// <param name="ArtifactRef">Docker image reference or native bundle URL</param>
/// <param name="ArtifactSha256">OCI index digest (sha256:...) or 64 lowercase hex of the native bundle</param>
/// <param name="OldState">Facts about the old engine needed to revert</param>
/// <param name="NewState">Facts about what was created for the new engine</param>
public record EngineUpgradeContext(
    Guid OperationId,
    int FromMajor,
    int ToMajor,
    string ArtifactRef,
    string ArtifactSha256,
    IDictionary<string, string> OldState,
    IDictionary<string, string> NewState);
