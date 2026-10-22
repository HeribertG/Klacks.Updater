// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Starts/stops an STT engine's compose service (Whisper or Parakeet) via the docker CLI. The compose profile is passed
/// explicitly so the service starts even though it is profile-gated in the customer compose file.
/// Start persists the profile into the compose .env (COMPOSE_PROFILES) so manual compose runs keep
/// the service; stop removes it again.
/// </summary>
/// <param name="options">Deployment configuration (compose dir)</param>
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

    public bool Supports(SttEngineProfile profile) => true;

    public async Task StartAsync(SttEngineProfile profile, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting STT service {Service}.", profile.ServiceName);
        await ProcessRunner.RunAsync(
            "docker",
            $"compose --profile {profile.ComposeProfile} up -d {profile.ServiceName}",
            _options.ComposeProjectDir,
            cancellationToken: cancellationToken);
        await PersistProfileAsync(profile, enable: true, cancellationToken);
    }

    public async Task StopAsync(SttEngineProfile profile, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping STT service {Service}.", profile.ServiceName);
        await ProcessRunner.RunAsync(
            "docker",
            $"compose --profile {profile.ComposeProfile} stop {profile.ServiceName}",
            _options.ComposeProjectDir,
            cancellationToken: cancellationToken);
        await PersistProfileAsync(profile, enable: false, cancellationToken);
    }

    private async Task PersistProfileAsync(SttEngineProfile profile, bool enable, CancellationToken cancellationToken)
    {
        var envPath = Path.Combine(_options.ComposeProjectDir, ".env");
        try
        {
            var lines = File.Exists(envPath)
                ? await File.ReadAllLinesAsync(envPath, cancellationToken)
                : [];

            var updated = enable
                ? ComposeEnvFile.WithProfile(lines, profile.ComposeProfile)
                : ComposeEnvFile.WithoutProfile(lines, profile.ComposeProfile);

            if (!ReferenceEquals(updated, lines))
            {
                await File.WriteAllLinesAsync(envPath, updated, cancellationToken);
                _logger.LogInformation("Persisted COMPOSE_PROFILES change ({Profile}, enable={Enable}) in {Path}.", profile.ComposeProfile, enable, envPath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not persist COMPOSE_PROFILES in {Path}; manual compose runs may not include the {Profile} profile.", envPath, profile.ComposeProfile);
        }
    }
}
