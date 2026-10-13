// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Polls the internal deep health endpoint until it reports healthy (HTTP 200) and the version endpoint reports the
/// expected major.minor.patch, or the gate timeout elapses. The version check catches an old process that still
/// answers (a stop that did not take effect) and an artifact built with the wrong version. A prerelease suffix is
/// ignored because the API embeds only the numeric version; an unparsable expected version skips the check. The
/// timeout deliberately exceeds the slowest migration so the gate never fails mid-migration.
/// </summary>
/// <param name="httpClientFactory">Factory for the health HTTP client</param>
/// <param name="options">Deployment configuration (health URL)</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
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

    public async Task<bool> WaitForHealthyAsync(string expectedVersion, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var expected = ParseNumericVersion(expectedVersion);
        var deadline = DateTime.UtcNow.Add(UpdaterConstants.HealthGateTimeout);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await client.GetAsync(_options.HealthUrl, cancellationToken);
                if (response.IsSuccessStatusCode && await RunsExpectedVersionAsync(client, expected, cancellationToken))
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or NotSupportedException)
            {
                // API is restarting / not yet reachable — keep polling until the deadline.
            }

            await Task.Delay(UpdaterConstants.HealthPollInterval, cancellationToken);
        }

        _logger.LogWarning("Health gate timed out after {Timeout} waiting for version {Version}.", UpdaterConstants.HealthGateTimeout, expectedVersion);
        return false;
    }

    private async Task<bool> RunsExpectedVersionAsync(HttpClient client, (int Major, int Minor, int Patch)? expected, CancellationToken cancellationToken)
    {
        if (expected is null)
        {
            return true;
        }

        var running = await client.GetFromJsonAsync<RunningVersion>(ResolveVersionUrl(_options), cancellationToken);
        var matches = running is not null
            && (running.Major, running.Minor, running.Patch) == expected.Value;
        if (!matches)
        {
            _logger.LogInformation(
                "Healthy, but running version {Major}.{Minor}.{Patch} is not the expected {Expected} yet.",
                running?.Major, running?.Minor, running?.Patch, expected);
        }

        return matches;
    }

    public static string ResolveVersionUrl(UpdaterOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.VersionUrl))
        {
            return options.VersionUrl;
        }

        return new Uri(new Uri(options.HealthUrl), UpdaterConstants.VersionPath).AbsoluteUri;
    }

    public static (int Major, int Minor, int Patch)? ParseNumericVersion(string version)
    {
        var core = (version ?? string.Empty).Split('-')[0].Split('.');
        if (core.Length != 3
            || !int.TryParse(core[0], out var major)
            || !int.TryParse(core[1], out var minor)
            || !int.TryParse(core[2], out var patch))
        {
            return null;
        }

        return (major, minor, patch);
    }

    private sealed record RunningVersion(
        [property: JsonPropertyName("major")] int Major,
        [property: JsonPropertyName("minor")] int Minor,
        [property: JsonPropertyName("patch")] int Patch);
}
