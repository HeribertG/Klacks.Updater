// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Domain;

/// <summary>
/// A claimed update_history row as the executor needs it. The rollback target version is
/// FromVersion for an Update operation (the version it replaced) and TargetVersion for an explicit
/// Rollback operation (the version to restore to).
/// </summary>
public record UpdateOperation
{
    public required Guid Id { get; init; }

    public required UpdateOperationType OperationType { get; init; }

    public string FromVersion { get; init; } = string.Empty;

    public string TargetVersion { get; init; } = string.Empty;

    public string? ArtifactRef { get; init; }

    public string? ArtifactSha256 { get; init; }

    public string? ArtifactSignature { get; init; }

    public bool ContainsMigrations { get; init; }

    public string? BackupRef { get; init; }

    public string RollbackTargetVersion =>
        OperationType == UpdateOperationType.Update ? FromVersion : TargetVersion;
}
