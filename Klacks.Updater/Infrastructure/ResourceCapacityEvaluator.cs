// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Compares measured free memory and disk against what an STT engine needs and builds the English rejection message.
/// A value that could not be measured (null) is skipped, so a failed probe never blocks an install. ResolveProbeRoot
/// picks the drive whose free space matters: the one that holds the models (data root), else the program root.
/// </summary>
namespace Klacks.Updater.Infrastructure;

public static class ResourceCapacityEvaluator
{
    private const long BytesPerMegaByte = 1024 * 1024;

    public static string? ResolveProbeRoot(string? dataRoot, string? programRoot)
    {
        foreach (var candidate in new[] { dataRoot, programRoot })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var root = Path.GetPathRoot(candidate);
            if (!string.IsNullOrEmpty(root))
            {
                return root;
            }
        }

        return null;
    }

    public static string? Evaluate(long? availableMemoryBytes, long? availableDiskBytes, long requiredMemoryBytes, long requiredDiskBytes)
    {
        if (availableMemoryBytes is not null && availableMemoryBytes < requiredMemoryBytes)
        {
            return $"Not enough free memory: {availableMemoryBytes / BytesPerMegaByte} MB available, {requiredMemoryBytes / BytesPerMegaByte} MB required.";
        }

        if (availableDiskBytes is not null && availableDiskBytes < requiredDiskBytes)
        {
            return $"Not enough free disk space: {availableDiskBytes / BytesPerMegaByte} MB available, {requiredDiskBytes / BytesPerMegaByte} MB required.";
        }

        return null;
    }
}
