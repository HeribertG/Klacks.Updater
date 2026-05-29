// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Polls the internal deep health endpoint until it reports healthy (HTTP 200) or the gate timeout
/// elapses. The timeout deliberately exceeds the slowest migration so the gate never fails mid-migration.
/// </summary>
/// <param name="httpClientFactory">Factory for the health HTTP client</param>
/// <param name="options">Deployment configuration (health URL)</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.Net.Http;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class HttpHealthGate : IUpdateHealthGate
{
    public const string HttpClientName = "UpdateHealth";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UpdaterOptions _options;
    private readonly ILogger<HttpHealthGate> _logger;

    public HttpHealthGate(IHttpClientFactory httpClientFactory, IOptions<UpdaterOptions> options, ILogger<HttpHealthGate> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> WaitForHealthyAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var deadline = DateTime.UtcNow.Add(UpdaterConstants.HealthGateTimeout);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(_options.HealthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (HttpRequestException)
            {
                // API is restarting / not yet reachable — keep polling until the deadline.
            }

            await Task.Delay(UpdaterConstants.HealthPollInterval, cancellationToken);
        }

        _logger.LogWarning("Health gate timed out after {Timeout}.", UpdaterConstants.HealthGateTimeout);
        return false;
    }
}
