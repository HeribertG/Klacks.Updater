// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Keeps the klacks-stt compose service at the version the activated API image pins in its org.klacks.stt-version label
/// (the API image digest is signed in the update manifest, so the label is integrity-protected). PlanAsync reads and
/// validates the label of the pulled API image and compares it with the tag recorded in the compose .env; ApplyAsync pulls
/// and restarts the service only when its compose profile is active (COMPOSE_PROFILES in .env) and the tag changed. Old API
/// images without the label leave the tag untouched. Every failure is logged as a warning and swallowed.
/// </summary>
/// <param name="options">Deployment configuration (compose dir, API image repository, speech service names, tag env var)</param>
/// <param name="runner">Runs the docker CLI</param>
/// <param name="logger">Logger for diagnostic output</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class DockerSttTagSynchronizer : ISttTagSynchronizer
{
    private const string Docker = "docker";
    private const string ImageRefSeparator = ":";
    private const string EnvFileName = ".env";
    private const string InspectFormat = "{{ index .Config.Labels \"" + SttVersionLabel.Key + "\" }}";

    private readonly UpdaterOptions _options;
    private readonly IProcessRunner _runner;
    private readonly ILogger<DockerSttTagSynchronizer> _logger;

    public DockerSttTagSynchronizer(IOptions<UpdaterOptions> options, IProcessRunner runner, ILogger<DockerSttTagSynchronizer> logger)
    {
        _options = options.Value;
        _runner = runner;
        _logger = logger;
    }

    public async Task<SttTagUpdate?> PlanAsync(string apiTag, CancellationToken cancellationToken = default)
    {
        var imageRef = $"{_options.ApiImageRepository}{ImageRefSeparator}{apiTag}";
        try
        {
            var output = await _runner.RunAsync(
                Docker,
                ["image", "inspect", "--format", InspectFormat, imageRef],
                _options.ComposeProjectDir,
                cancellationToken: cancellationToken);

            var sttTag = SttVersionLabel.Parse(output);
            if (sttTag is null)
            {
                _logger.LogInformation("Image {Image} carries no valid {Label} label; {Variable} stays unchanged.", imageRef, SttVersionLabel.Key, _options.SttTagEnvVar);
                return null;
            }

            var lines = await ReadEnvLinesAsync(cancellationToken);
            return new SttTagUpdate(
                sttTag,
                ComposeEnvFile.GetValue(lines, _options.SttTagEnvVar),
                ComposeEnvFile.HasProfile(lines, ResolveProfile().ComposeProfile));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not determine the speech service version pinned by {Image}; {Variable} stays unchanged.", imageRef, _options.SttTagEnvVar);
            return null;
        }
    }

    public async Task ApplyAsync(SttTagUpdate update, IDictionary<string, string> environment, CancellationToken cancellationToken = default)
    {
        var profile = ResolveProfile();
        if (!update.RequiresRestart)
        {
            _logger.LogInformation(
                "Speech service {Service} not restarted (active={Active}, tag {Tag}, previous {Previous}).",
                profile.ServiceName, update.IsServiceActive, update.Tag, update.PreviousTag ?? "unknown");
            return;
        }

        try
        {
            _logger.LogInformation("Updating speech service {Service} to tag {Tag}.", profile.ServiceName, update.Tag);
            await _runner.RunAsync(
                Docker,
                ["compose", "--profile", profile.ComposeProfile, "pull", profile.ServiceName],
                _options.ComposeProjectDir,
                environment,
                cancellationToken);
            await _runner.RunAsync(
                Docker,
                ["compose", "--profile", profile.ComposeProfile, "up", "-d", profile.ServiceName],
                _options.ComposeProjectDir,
                environment,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Updating speech service {Service} to tag {Tag} failed; the API update is unaffected.", profile.ServiceName, update.Tag);
        }
    }

    private SttEngineProfile ResolveProfile() =>
        SttEngineProfiles.Resolve(WhisperPluginConstants.ModelAliasParakeet, _options)!;

    private async Task<string[]> ReadEnvLinesAsync(CancellationToken cancellationToken)
    {
        var envPath = Path.Combine(_options.ComposeProjectDir, EnvFileName);
        return File.Exists(envPath)
            ? await File.ReadAllLinesAsync(envPath, cancellationToken)
            : [];
    }
}
