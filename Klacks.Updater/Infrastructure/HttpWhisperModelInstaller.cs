// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// HTTP client for Speaches-compatible STT services (Speaches, Klacks.Stt): polls GET /v1/models on the given base URL
/// until the service answers, then installs a model via POST /v1/models/{id} (slash in the model id URL-encoded).
/// Conflict responses count as success so re-installing an already downloaded model is idempotent; a 404 raises
/// SttModelNotFoundException so callers can tell an unknown model from a failed download.
/// </summary>
/// <param name="httpClientFactory">Factory for the STT HTTP client (long timeout for model downloads)</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.Net;
using System.Net.Http;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;

namespace Klacks.Updater.Infrastructure;

public class HttpWhisperModelInstaller : IWhisperModelInstaller
{
    public const string HttpClientName = "WhisperPlugin";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpWhisperModelInstaller> _logger;

    public HttpWhisperModelInstaller(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpWhisperModelInstaller> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<bool> WaitForReadyAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = $"{baseUrl.TrimEnd('/')}{WhisperPluginConstants.ModelsPath}";
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

        _logger.LogWarning("STT service at {Url} did not become ready within {Timeout}.", baseUrl, UpdaterConstants.WhisperReadyTimeout);
        return false;
    }

    public async Task InstallModelAsync(string baseUrl, string modelId, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var url = $"{baseUrl.TrimEnd('/')}{WhisperPluginConstants.ModelsPath}/{Uri.EscapeDataString(modelId)}";

        _logger.LogInformation("Installing STT model {Model} (first download can take several minutes).", modelId);
        using var response = await client.PostAsync(url, content: null, cancellationToken);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
        {
            _logger.LogInformation("STT model {Model} installed ({Status}).", modelId, response.StatusCode);
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = $"Model install for '{modelId}' failed with {(int)response.StatusCode}: {Truncate(body)}";
        throw response.StatusCode == HttpStatusCode.NotFound
            ? new SttModelNotFoundException(message)
            : new InvalidOperationException(message);
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300];
}
