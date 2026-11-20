// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Strict format check of the signed fields of an update row, applied before any signature verification so that no
/// field can be crafted to reproduce the newline-separated payload of another operation type. Application updates
/// and rollbacks need a release version (1.2.3 or 1.2.3-rc.1) as target_version; a PostgreSQL engine upgrade needs
/// majors of one to three digits as from_version and target_version; no signed field may contain a control
/// character. Speech-to-text operations are not signed; their target_version is checked against the fixed list of
/// model aliases by the STT executor.
/// </summary>
using System.Text.RegularExpressions;
using Klacks.Updater.Domain;

namespace Klacks.Updater.Application;

public static partial class SignedFieldFormat
{
    public static bool IsValid(UpdateOperation operation)
    {
        if (HasControlCharacter(operation.TargetVersion)
            || HasControlCharacter(operation.FromVersion)
            || HasControlCharacter(operation.ArtifactRef)
            || HasControlCharacter(operation.ArtifactSha256))
        {
            return false;
        }

        return operation.OperationType switch
        {
            UpdateOperationType.Update or UpdateOperationType.Rollback => ReleaseVersionPattern().IsMatch(operation.TargetVersion),
            UpdateOperationType.PostgresUpgrade => EngineMajorPattern().IsMatch(operation.FromVersion) && EngineMajorPattern().IsMatch(operation.TargetVersion),
            _ => true,
        };
    }

    private static bool HasControlCharacter(string? value) => value is not null && value.Any(char.IsControl);

    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseVersionPattern();

    [GeneratedRegex(@"^[0-9]{1,3}\z", RegexOptions.CultureInvariant)]
    private static partial Regex EngineMajorPattern();
}
