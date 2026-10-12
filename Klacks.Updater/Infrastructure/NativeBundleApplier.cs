// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Native applier: downloads the release bundle, refuses it unless its SHA-256 matches the signed hash on the update
/// row, unpacks it into versions/&lt;target&gt; via a staging directory, then stops the service, points "current" at the
/// new version and starts it again (the API migrates on start). A rollback only switches "current" back to a version
/// that is still on disk; old versions beyond the retention count are removed after a successful switch, never the
/// current or the previous one.
/// </summary>
/// <param name="options">Updater configuration (program root, retained versions)</param>
/// <param name="httpClientFactory">Creates the client for the bundle download</param>
/// <param name="serviceController">Stops and starts the Klacks service</param>
/// <param name="linkSwitcher">Points "current" at a version directory</param>
/// <param name="logger">Logger for diagnostic output</param>

using System.IO.Compression;
using System.Security.Cryptography;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class NativeBundleApplier : IUpdateApplier
{
    public const string HttpClientName = "native-bundle";

    private const string BundleFileName = "bundle.zip";
    private const string ExtractDirectoryName = "extracted";

    private readonly UpdaterOptions _options;
    private readonly NativeInstallLayout _layout;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceController _serviceController;
    private readonly IVersionLinkSwitcher _linkSwitcher;
    private readonly ILogger<NativeBundleApplier> _logger;

    public NativeBundleApplier(
        IOptions<UpdaterOptions> options,
        IHttpClientFactory httpClientFactory,
        IServiceController serviceController,
        IVersionLinkSwitcher linkSwitcher,
        ILogger<NativeBundleApplier> logger)
    {
        _options = options.Value;
        _layout = new NativeInstallLayout(_options.NativeProgramRoot);
        _httpClientFactory = httpClientFactory;
        _serviceController = serviceController;
        _linkSwitcher = linkSwitcher;
        _logger = logger;
    }

    public async Task ActivateAsync(UpdateOperation operation, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operation.ArtifactRef) || string.IsNullOrWhiteSpace(operation.ArtifactSha256))
        {
            throw new InvalidOperationException("Native update needs a bundle URL and its SHA-256.");
        }

        var versionDirectory = _layout.VersionDirectory(operation.TargetVersion);
        if (!Directory.Exists(versionDirectory))
        {
            await InstallBundleAsync(operation.ArtifactRef, operation.ArtifactSha256, versionDirectory, cancellationToken);
        }

        await SwitchToAsync(versionDirectory, cancellationToken);
        RemoveOldVersions(versionDirectory);
    }

    public Task ActivateVersionAsync(string version, CancellationToken cancellationToken = default)
    {
        var versionDirectory = _layout.VersionDirectory(version);
        if (!Directory.Exists(versionDirectory))
        {
            throw new DirectoryNotFoundException($"Version {version} is no longer installed; cannot switch back to it.");
        }

        return SwitchToAsync(versionDirectory, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _serviceController.StopAsync(cancellationToken);
    }

    private async Task InstallBundleAsync(
        string bundleUrl, string expectedSha256, string versionDirectory, CancellationToken cancellationToken)
    {
        var staging = Path.Combine(_layout.StagingDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var bundlePath = Path.Combine(staging, BundleFileName);
            await DownloadAsync(bundleUrl, bundlePath, cancellationToken);

            var actualSha256 = await ComputeSha256Async(bundlePath, cancellationToken);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Bundle hash mismatch: expected {expectedSha256}, got {actualSha256}. Nothing was installed.");
            }

            var extractDirectory = Path.Combine(staging, ExtractDirectoryName);
            ZipFile.ExtractToDirectory(bundlePath, extractDirectory);
            Directory.CreateDirectory(_layout.VersionsDirectory);
            Directory.Move(extractDirectory, versionDirectory);
            _logger.LogInformation("Installed bundle {Url} into {Directory}.", bundleUrl, versionDirectory);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private async Task DownloadAsync(string bundleUrl, string targetPath, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.GetAsync(bundleUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, cancellationToken);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private async Task SwitchToAsync(string versionDirectory, CancellationToken cancellationToken)
    {
        await _serviceController.StopAsync(cancellationToken);
        await _linkSwitcher.SwitchAsync(_layout.CurrentLink, versionDirectory, cancellationToken);
        _logger.LogInformation("Switched {Link} to {Directory}.", _layout.CurrentLink, versionDirectory);
        await _serviceController.StartAsync(cancellationToken);
    }

    private void RemoveOldVersions(string currentDirectory)
    {
        var keep = Math.Max(_options.NativeRetainedVersions, 2);
        var obsolete = new DirectoryInfo(_layout.VersionsDirectory)
            .EnumerateDirectories()
            .Where(directory => !string.Equals(directory.FullName, currentDirectory, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(directory => directory.CreationTimeUtc)
            .Skip(keep - 1);

        foreach (var directory in obsolete)
        {
            try
            {
                directory.Delete(recursive: true);
                _logger.LogInformation("Removed old version directory {Directory}.", directory.FullName);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not remove old version directory {Directory}.", directory.FullName);
            }
        }
    }
}
