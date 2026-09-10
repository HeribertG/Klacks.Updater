// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Speaches HTTP client: polls GET /v1/models until the container answers, then installs a model via
/// POST /v1/models/{id} (slash in the model id URL-encoded). Conflict responses count as success so
/// re-installing an already downloaded model is idempotent.
/// </summary>
/// <param name="httpClientFactory">Factory for the whisper HTTP client (long timeout for model downloads)</param>
/// <param name="options">Deployment configuration (whisper base URL)</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.Net;
using System.Net.Http;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class HttpWhisperModelInstaller : IWhisperModelInstaller
{
    public const string HttpClientName = "WhisperPlugin";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UpdaterOptions _options;
    private readonly ILogger<HttpWhisperModelInstaller> _logger;

    public HttpWhisperModelInstaller(
        IHttpClientFactory httpClientFactory,
        IOptions<UpdaterOptions> options,
        ILogger<HttpWhisperModelInstaller> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = $"{_options.WhisperBaseUrl.TrimEnd('/')}{WhisperPluginConstants.ModelsPath}";
        var deadline = DateTime.UtcNow.Add(UpdaterConstants.WhisperReadyTimeout);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // Container is still starting — keep polling until the deadline.
            }

            await Task.Delay(UpdaterConstants.HealthPollInterval, cancellationToken);
        }

        _logger.LogWarning("Whisper service did not become ready within {Timeout}.", UpdaterConstants.WhisperReadyTimeout);
        return false;
    }

    public async Task InstallModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = $"{_options.WhisperBaseUrl.TrimEnd('/')}{WhisperPluginConstants.ModelsPath}/{Uri.EscapeDataString(modelId)}";

        _logger.LogInformation("Installing Whisper model {Model} (first download can take several minutes).", modelId);
        using var response = await client.PostAsync(url, content: null, cancellationToken);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation("Whisper model {Model} installed ({Status}).", modelId, response.StatusCode);
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Model install for '{modelId}' failed with {(int)response.StatusCode}: {Truncate(body)}");
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];
}
