// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Starts/stops the Whisper compose service via the docker CLI. The compose profile is passed
/// explicitly so the service starts even though it is profile-gated in the customer compose file.
/// Start persists the profile into the compose .env (COMPOSE_PROFILES) so manual compose runs keep
/// the service; stop removes it again.
/// </summary>
/// <param name="options">Deployment configuration (compose dir, whisper service name and profile)</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class DockerWhisperApplier : IWhisperPluginApplier
{
    private readonly UpdaterOptions _options;
    private readonly ILogger<DockerWhisperApplier> _logger;

    public DockerWhisperApplier(IOptions<UpdaterOptions> options, ILogger<DockerWhisperApplier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Whisper service {Service}.", _options.WhisperServiceName);
        await ProcessRunner.RunAsync(
            "docker",
            $"compose --profile {_options.WhisperComposeProfile} up -d {_options.WhisperServiceName}",
            _options.ComposeProjectDir,
            cancellationToken: cancellationToken);
        await PersistProfileAsync(enable: true, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping Whisper service {Service}.", _options.WhisperServiceName);
        await ProcessRunner.RunAsync(
            "docker",
            $"compose --profile {_options.WhisperComposeProfile} stop {_options.WhisperServiceName}",
            _options.ComposeProjectDir,
            cancellationToken: cancellationToken);
        await PersistProfileAsync(enable: false, cancellationToken);
    }

    private async Task PersistProfileAsync(bool enable, CancellationToken cancellationToken)
    {
        var envPath = Path.Combine(_options.ComposeProjectDir, ".env");
        try
        {
            var lines = File.Exists(envPath)
                ? await File.ReadAllLinesAsync(envPath, cancellationToken)
                : [];

            var updated = enable
                ? ComposeEnvFile.WithProfile(lines, _options.WhisperComposeProfile)
                : ComposeEnvFile.WithoutProfile(lines, _options.WhisperComposeProfile);

            if (!ReferenceEquals(updated, lines))
            {
                await File.WriteAllLinesAsync(envPath, updated, cancellationToken);
                _logger.LogInformation("Persisted COMPOSE_PROFILES change ({Profile}, enable={Enable}) in {Path}.", _options.WhisperComposeProfile, enable, envPath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not persist COMPOSE_PROFILES in {Path}; manual compose runs may not include the whisper profile.", envPath);
        }
    }
}
