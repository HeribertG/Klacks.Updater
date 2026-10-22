// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Stops the KlacksStt Windows service before a version switch when it is running and starts it again afterwards when
/// its start type is automatic (the updater sets that while the Parakeet engine is installed). A missing, disabled or
/// manually started service is left alone.
/// </summary>
/// <param name="options">Updater configuration; NativeSttServiceName names the Windows service</param>

using System.Runtime.Versioning;
using System.ServiceProcess;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

[SupportedOSPlatform("windows")]
public class WindowsSttServiceLifecycle : ISttServiceLifecycle
{
    private readonly string _serviceName;

    public WindowsSttServiceLifecycle(IOptions<UpdaterOptions> options)
    {
        _serviceName = options.Value.NativeSttServiceName;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!WindowsServices.Exists(_serviceName))
        {
            return Task.CompletedTask;
        }

        using var service = new ServiceController(_serviceName);
        if (service.Status is ServiceControllerStatus.Stopped)
        {
            return Task.CompletedTask;
        }

        if (service.Status is not ServiceControllerStatus.StopPending)
        {
            service.Stop();
        }

        service.WaitForStatus(ServiceControllerStatus.Stopped, UpdaterConstants.ServiceStateTimeout);
        return Task.CompletedTask;
    }

    public Task StartIfAutomaticAsync(CancellationToken cancellationToken = default)
    {
        if (!WindowsServices.Exists(_serviceName))
        {
            return Task.CompletedTask;
        }

        using var service = new ServiceController(_serviceName);
        if (service.StartType != ServiceStartMode.Automatic)
        {
            return Task.CompletedTask;
        }

        if (service.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending))
        {
            service.Start();
        }

        service.WaitForStatus(ServiceControllerStatus.Running, UpdaterConstants.ServiceStateTimeout);
        return Task.CompletedTask;
    }
}
