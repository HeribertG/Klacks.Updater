// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Canonical payload that the release signature covers: the target version, the resolved artifact
/// reference and its sha256, newline-separated. The vendor's CI signs exactly this string; the
/// updater verifies the same string from the claimed row. Keeping it here is the single source of
/// truth shared by signing and verification.
/// </summary>
using Klacks.Updater.Domain;

namespace Klacks.Updater.Application;

public static class ArtifactSignaturePayload
{
    public static string Build(UpdateOperation operation)
    {
        return $"{operation.TargetVersion}\n{operation.ArtifactRef}\n{operation.ArtifactSha256}";
    }
}
