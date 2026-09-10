// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Docker applier: activates an image tag for the API service via docker compose (pull then up -d),
/// always pulling first so a rollback target absent locally is fetched from the registry. Recreating
/// the API container triggers its on-boot migration. When a UI image repository is configured, the UI
/// service is activated at the same tag right after the API (so updates and rollbacks keep API and UI
/// in lockstep). Once both services run, the activated tags are persisted in the compose .env file so a
/// reboot or a manual `docker compose up` resolves the same version. Never touches the updater itself.
/// </summary>
/// <param name="options">Deployment configuration (compose dir, service name, image repository, tag env var)</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class DockerApplier : IUpdateApplier
{
    private readonly UpdaterOptions _options;
    private readonly ILogger<DockerApplier> _logger;

    public DockerApplier(IOptions<UpdaterOptions> options, ILogger<DockerApplier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task ActivateAsync(string imageRef, CancellationToken cancellationToken = default)
    {
        var tag = imageRef.Contains(':') ? imageRef[(imageRef.LastIndexOf(':') + 1)..] : imageRef;
        return ActivateTagAsync(tag, cancellationToken);
    }

    public Task ActivateVersionAsync(string version, CancellationToken cancellationToken = default)
    {
        return ActivateTagAsync(version, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await ProcessRunner.RunAsync("docker", $"compose stop {_options.ApiServiceName}", _options.ComposeProjectDir, cancellationToken: cancellationToken);
    }

    private async Task ActivateTagAsync(string tag, CancellationToken cancellationToken)
    {
        var hasUi = !string.IsNullOrWhiteSpace(_options.UiImageRepository);
        var env = new Dictionary<string, string> { [_options.ApiTagEnvVar] = tag };
        if (hasUi)
        {
            env[_options.UiTagEnvVar] = tag;
        }

        _logger.LogInformation("Activating {Service} tag {Tag}.", _options.ApiServiceName, tag);
        await ProcessRunner.RunAsync("docker", $"compose pull {_options.ApiServiceName}", _options.ComposeProjectDir, env, cancellationToken);
        await ProcessRunner.RunAsync("docker", $"compose up -d {_options.ApiServiceName}", _options.ComposeProjectDir, env, cancellationToken);

        if (hasUi)
        {
            _logger.LogInformation("Activating {Service} tag {Tag}.", _options.UiServiceName, tag);
            await ProcessRunner.RunAsync("docker", $"compose pull {_options.UiServiceName}", _options.ComposeProjectDir, env, cancellationToken);
            await ProcessRunner.RunAsync("docker", $"compose up -d {_options.UiServiceName}", _options.ComposeProjectDir, env, cancellationToken);
        }

        await PersistTagsAsync(env, cancellationToken);
    }

    // Written only after the services are up: until 2026-09-10 the tags lived solely in the process
    // environment of the compose calls, so the .env file on the Hetzner host kept pointing at the
    // previous version and a reboot would have started it again. Failing to persist is logged, not
    // fatal - the running services are already correct, only a later manual compose run would drift.
    private async Task PersistTagsAsync(IReadOnlyDictionary<string, string> tags, CancellationToken cancellationToken)
    {
        var envPath = Path.Combine(_options.ComposeProjectDir, ".env");
        try
        {
            var lines = File.Exists(envPath)
                ? await File.ReadAllLinesAsync(envPath, cancellationToken)
                : [];

            var updated = lines;
            foreach (var (key, value) in tags)
            {
                updated = ComposeEnvFile.WithValue(updated, key, value);
            }

            if (!ReferenceEquals(updated, lines))
            {
                await File.WriteAllLinesAsync(envPath, updated, cancellationToken);
                _logger.LogInformation("Persisted image tags {Tags} in {Path}.", string.Join(", ", tags.Select(t => $"{t.Key}={t.Value}")), envPath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not persist image tags in {Path}; a manual compose run may start the previous version.", envPath);
        }
    }
}
