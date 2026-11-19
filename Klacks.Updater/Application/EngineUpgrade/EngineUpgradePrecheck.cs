// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Pure prechecks of an engine upgrade request; they run before anything is changed. A non-null result is the reason
/// the operation is rejected: unparsable or non-increasing majors, a source major that differs from the running
/// server or the target's view, an unsupported deployment, roles other than the bootstrap superuser (v1 limit),
/// too little free space (at least max(2 GB, 3 x the database size)), an artifact digest of the wrong form, or an
/// application version that cannot be read for the health gate.
/// </summary>
using System.Globalization;
using System.Text.RegularExpressions;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;

namespace Klacks.Updater.Application.EngineUpgrade;

public static partial class EngineUpgradePrecheck
{
    public static bool TryParseMajors(UpdateOperation operation, out int fromMajor, out int toMajor)
    {
        toMajor = 0;
        return int.TryParse(operation.FromVersion, NumberStyles.None, CultureInfo.InvariantCulture, out fromMajor)
            && int.TryParse(operation.TargetVersion, NumberStyles.None, CultureInfo.InvariantCulture, out toMajor);
    }

    public static long RequiredFreeBytes(long totalDatabaseBytes) =>
        Math.Max(EngineUpgradeConstants.MinimumFreeBytes, EngineUpgradeConstants.FreeSpaceDatabaseFactor * totalDatabaseBytes);

    public static bool IsValidArtifactDigest(string? sha256) =>
        !string.IsNullOrEmpty(sha256)
        && (DockerDigestPattern().IsMatch(sha256) || HexDigestPattern().IsMatch(sha256));

    public static string? Evaluate(
        UpdateOperation operation,
        EngineInspection inspection,
        EngineClusterFacts facts,
        string? applicationVersion)
    {
        if (!TryParseMajors(operation, out var fromMajor, out var toMajor))
        {
            return $"from_version '{operation.FromVersion}' and target_version '{operation.TargetVersion}' must be PostgreSQL major numbers.";
        }

        if (toMajor <= fromMajor)
        {
            return $"Target major {toMajor} must be higher than source major {fromMajor}.";
        }

        if (inspection.UnsupportedReason is not null)
        {
            return inspection.UnsupportedReason;
        }

        if (facts.ServerMajor != fromMajor || inspection.CurrentMajor != fromMajor)
        {
            return $"from_version {fromMajor} does not match the running server (major {facts.ServerMajor}, deployment reports {inspection.CurrentMajor}).";
        }

        if (string.IsNullOrWhiteSpace(operation.ArtifactRef)
            || operation.ArtifactRef.Any(char.IsWhiteSpace)
            || !IsValidArtifactDigest(operation.ArtifactSha256))
        {
            return "The engine artifact reference or its sha256 digest is missing or malformed.";
        }

        if (facts.ForeignRoles.Count > 0)
        {
            return $"Roles other than the bootstrap superuser are not migrated by this version: {string.Join(", ", facts.ForeignRoles)}.";
        }

        if (facts.Databases.Count == 0)
        {
            return "No database to migrate was found.";
        }

        var required = RequiredFreeBytes(facts.TotalDatabaseBytes);
        if (inspection.FreeBytes < required)
        {
            return $"Not enough free space: {inspection.FreeBytes} bytes available, {required} bytes required.";
        }

        if (string.IsNullOrWhiteSpace(applicationVersion))
        {
            return "The running application version could not be read; the application must be healthy before an engine upgrade.";
        }

        return null;
    }

    [GeneratedRegex(@"^sha256:[0-9a-f]{64}\z")]
    private static partial Regex DockerDigestPattern();

    [GeneratedRegex(@"^[0-9a-f]{64}\z")]
    private static partial Regex HexDigestPattern();
}
