// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Compares measured free memory and disk against what an STT engine needs and builds the English rejection message.
/// A value that could not be measured (null) is skipped, so a failed probe never blocks an install.
/// </summary>
namespace Klacks.Updater.Infrastructure;

public static class ResourceCapacityEvaluator
{
    private const long BytesPerMegaByte = 1024 * 1024;

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
