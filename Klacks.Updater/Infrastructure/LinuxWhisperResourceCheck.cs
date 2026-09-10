// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Capacity gate using host-wide /proc/meminfo (not namespaced in containers) for free memory and
/// the compose directory's filesystem for free disk. Rejecting here keeps small hosts from swapping
/// themselves to death when a model container starts.
/// </summary>
/// <param name="options">Deployment configuration (compose dir used as the disk probe path)</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class LinuxWhisperResourceCheck : IWhisperResourceCheck
{
    private const string MemInfoPath = "/proc/meminfo";
    private const string MemAvailableKey = "MemAvailable:";
    private const long BytesPerKiloByte = 1024;
    private const long BytesPerMegaByte = 1024 * 1024;

    private readonly UpdaterOptions _options;
    private readonly ILogger<LinuxWhisperResourceCheck> _logger;

    public LinuxWhisperResourceCheck(IOptions<UpdaterOptions> options, ILogger<LinuxWhisperResourceCheck> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> CheckAsync(long requiredMemoryBytes, long requiredDiskBytes, CancellationToken cancellationToken = default)
    {
        var availableMemory = await ReadAvailableMemoryAsync(cancellationToken);
        if (availableMemory is not null && availableMemory < requiredMemoryBytes)
        {
            return $"Not enough free memory: {availableMemory / BytesPerMegaByte} MB available, {requiredMemoryBytes / BytesPerMegaByte} MB required.";
        }

        var availableDisk = ReadAvailableDisk();
        if (availableDisk is not null && availableDisk < requiredDiskBytes)
        {
            return $"Not enough free disk space: {availableDisk / BytesPerMegaByte} MB available, {requiredDiskBytes / BytesPerMegaByte} MB required.";
        }

        return null;
    }

    private async Task<long?> ReadAvailableMemoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            foreach (var line in await File.ReadAllLinesAsync(MemInfoPath, cancellationToken))
            {
                if (!line.StartsWith(MemAvailableKey, StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && long.TryParse(parts[1], out var kiloBytes))
                {
                    return kiloBytes * BytesPerKiloByte;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read {Path}; skipping memory check.", MemInfoPath);
        }

        return null;
    }

    private long? ReadAvailableDisk()
    {
        try
        {
            return new DriveInfo(_options.ComposeProjectDir).AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine free disk space for {Path}; skipping disk check.", _options.ComposeProjectDir);
            return null;
        }
    }
}
