// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Reads major.minor.patch from the API version endpoint (same URL resolution as the health gate). Returns null when
/// the API is not reachable or answers with something else, so the caller can refuse to start an engine upgrade.
/// </summary>
/// <param name="httpClientFactory">Factory for the health HTTP client</param>
/// <param name="options">Deployment configuration (health and version URL)</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.Globalization;
using System.Text.Json;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class HttpApplicationVersionReader : IApplicationVersionReader
{
    private const string MajorProperty = "major";
    private const string MinorProperty = "minor";
    private const string PatchProperty = "patch";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly UpdaterOptions _options;
    private readonly ILogger<HttpApplicationVersionReader> _logger;

    public HttpApplicationVersionReader(IHttpClientFactory httpClientFactory, IOptions<UpdaterOptions> options, ILogger<HttpApplicationVersionReader> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> ReadRunningVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpHealthGate.HttpClientName);
            using var response = await client.GetAsync(HttpHealthGate.ResolveVersionUrl(_options), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            return string.Join(
                '.',
                root.GetProperty(MajorProperty).GetInt32().ToString(CultureInfo.InvariantCulture),
                root.GetProperty(MinorProperty).GetInt32().ToString(CultureInfo.InvariantCulture),
                root.GetProperty(PatchProperty).GetInt32().ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Reading the running application version failed.");
            return null;
        }
    }
}
