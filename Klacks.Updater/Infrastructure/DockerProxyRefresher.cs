// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Restarts the reverse proxy service after the API container was started or recreated. nginx keeps the address it
/// resolved for klacks-api at startup (unless its template re-resolves), and a recreated or restarted API container can
/// come back with another address; the proxy then answers 502 although the API runs. The restart also renders a changed
/// nginx template. Best effort: a failure is logged as a warning and never fails the update, the API itself is already up.
/// Skipped when no proxy service is configured.
/// </summary>
/// <param name="runner">Runs the docker CLI</param>
/// <param name="options">Compose project directory and proxy service name</param>
/// <param name="logger">Logger for the warning on failure</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;

namespace Klacks.Updater.Infrastructure;

public static class DockerProxyRefresher
{
    private const string Docker = "docker";

    public static async Task RefreshAsync(IProcessRunner runner, UpdaterOptions options, ILogger logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ProxyServiceName))
        {
            return;
        }

        try
        {
            await runner.RunAsync(Docker, ["compose", "restart", options.ProxyServiceName], options.ComposeProjectDir, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Restarting the proxy service {Service} failed; restart it by hand if the application answers 502.", options.ProxyServiceName);
        }
    }
}
