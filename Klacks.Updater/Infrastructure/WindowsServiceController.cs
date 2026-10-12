// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Stops and starts the Klacks Windows service and waits until it has reached the requested state. Stopping a
/// service that is already stopped is not an error, so a rollback after a failed start still proceeds.
/// </summary>
/// <param name="options">Updater configuration; NativeServiceName names the Windows service</param>

using System.Runtime.Versioning;
using System.ServiceProcess;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

[SupportedOSPlatform("windows")]
public class WindowsServiceController : IServiceController
{
    private readonly string _serviceName;

    public WindowsServiceController(IOptions<UpdaterOptions> options)
    {
        _serviceName = options.Value.NativeServiceName;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        using var service = new ServiceController(_serviceName);
        if (service.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
        {
            service.Stop();
        }

        service.WaitForStatus(ServiceControllerStatus.Stopped, UpdaterConstants.ServiceStateTimeout);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        using var service = new ServiceController(_serviceName);
        if (service.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending))
        {
            service.Start();
        }

        service.WaitForStatus(ServiceControllerStatus.Running, UpdaterConstants.ServiceStateTimeout);
        return Task.CompletedTask;
    }
}
