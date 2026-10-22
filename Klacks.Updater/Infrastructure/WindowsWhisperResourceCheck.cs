// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Capacity gate for native Windows installations: free physical memory from kernel32 GlobalMemoryStatusEx and free
/// disk of the drive that holds the models (the data root, else the program root). Probes that fail are skipped with a warning so they never block an
/// install.
/// </summary>
/// <param name="options">Deployment configuration (NativeProgramRoot is the fallback drive probed for free disk)</param>
/// <param name="configuration">Host configuration; Hosting:DataRoot holds the models and selects the drive probed first</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

[SupportedOSPlatform("windows")]
public class WindowsWhisperResourceCheck : IWhisperResourceCheck
{
    private const string Kernel32 = "kernel32.dll";

    private readonly UpdaterOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WindowsWhisperResourceCheck> _logger;

    public WindowsWhisperResourceCheck(
        IOptions<UpdaterOptions> options,
        IConfiguration configuration,
        ILogger<WindowsWhisperResourceCheck> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public Task<string?> CheckAsync(long requiredMemoryBytes, long requiredDiskBytes, CancellationToken cancellationToken = default)
    {
        var message = ResourceCapacityEvaluator.Evaluate(
            ReadAvailableMemory(),
            ReadAvailableDisk(),
            requiredMemoryBytes,
            requiredDiskBytes);

        return Task.FromResult(message);
    }

    [DllImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private long? ReadAvailableMemory()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                return (long)status.AvailablePhysicalBytes;
            }

            _logger.LogWarning("GlobalMemoryStatusEx failed (error {Error}); skipping memory check.", Marshal.GetLastWin32Error());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read free physical memory; skipping memory check.");
        }

        return null;
    }

    private long? ReadAvailableDisk()
    {
        var root = ResourceCapacityEvaluator.ResolveProbeRoot(
            _configuration[UpdaterConstants.NativeDataRootKey],
            _options.NativeProgramRoot);
        if (root is null)
        {
            _logger.LogWarning("Neither the data root nor NativeProgramRoot is configured; skipping disk check.");
            return null;
        }

        try
        {
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine free disk space for {Path}; skipping disk check.", root);
            return null;
        }
    }
}
