// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Stops and starts the Klacks launchd daemon (macOS). Stopping unloads the daemon so KeepAlive cannot restart it
/// mid-update; starting loads the installed property list again. Unloading a daemon that is not loaded is ignored.
/// </summary>
/// <param name="options">Updater configuration; NativeServiceName is the launchd label</param>

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public class LaunchdServiceController : IServiceController
{
    private const string LaunchCtl = "launchctl";
    private const string SystemDomain = "system";
    private const string LaunchDaemonsDirectory = "/Library/LaunchDaemons";
    private const string PropertyListExtension = ".plist";

    private readonly string _label;

    public LaunchdServiceController(IOptions<UpdaterOptions> options)
    {
        _label = options.Value.NativeServiceName;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ProcessRunner.RunAsync(LaunchCtl, $"bootout {SystemDomain}/{_label}", cancellationToken: cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // Not loaded: already stopped.
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var propertyList = Path.Combine(LaunchDaemonsDirectory, _label + PropertyListExtension);
        await ProcessRunner.RunAsync(LaunchCtl, $"bootstrap {SystemDomain} \"{propertyList}\"", cancellationToken: cancellationToken);
    }
}
