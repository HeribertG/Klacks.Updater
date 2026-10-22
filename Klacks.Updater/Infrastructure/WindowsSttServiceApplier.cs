// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Starts and stops the native Klacks.Stt Windows service (Parakeet). The service ships registered but disabled; start
/// switches it to automatic and runs it, stop halts it and disables it again so an uninstalled engine does not come
/// back after a reboot. Whisper is Docker-only and therefore not supported here.
/// </summary>
/// <param name="options">Updater configuration; NativeSttServiceName names the Windows service</param>
/// <param name="logger">Logger for diagnostic output</param>
using System.Runtime.Versioning;
using System.ServiceProcess;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

[SupportedOSPlatform("windows")]
public class WindowsSttServiceApplier : IWhisperPluginApplier
{
    private const string ServiceControlExecutable = "sc.exe";
    private const string ConfigCommand = "config";
    private const string StartAuto = "start= auto";
    private const string StartDisabled = "start= disabled";

    private readonly UpdaterOptions _options;
    private readonly ILogger<WindowsSttServiceApplier> _logger;

    public WindowsSttServiceApplier(IOptions<UpdaterOptions> options, ILogger<WindowsSttServiceApplier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool Supports(SttEngineProfile profile) => profile.IsParakeet;

    public async Task StartAsync(SttEngineProfile profile, CancellationToken cancellationToken = default)
    {
        var serviceName = _options.NativeSttServiceName;
        _logger.LogInformation("Starting native STT service {Service}.", serviceName);

        await ProcessRunner.RunAsync(
            ServiceControlExecutable,
            $"{ConfigCommand} {serviceName} {StartAuto}",
            cancellationToken: cancellationToken);

        using var service = new ServiceController(serviceName);
        if (service.Status is not (ServiceControllerStatus.Running or ServiceControllerStatus.StartPending))
        {
            service.Start();
        }

        service.WaitForStatus(ServiceControllerStatus.Running, UpdaterConstants.ServiceStateTimeout);
    }

    public async Task StopAsync(SttEngineProfile profile, CancellationToken cancellationToken = default)
    {
        var serviceName = _options.NativeSttServiceName;
        _logger.LogInformation("Stopping native STT service {Service}.", serviceName);

        try
        {
            using var service = new ServiceController(serviceName);
            if (service.Status is not (ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending))
            {
                service.Stop();
            }

            service.WaitForStatus(ServiceControllerStatus.Stopped, UpdaterConstants.ServiceStateTimeout);
        }
        finally
        {
            await ProcessRunner.RunAsync(
                ServiceControlExecutable,
                $"{ConfigCommand} {serviceName} {StartDisabled}",
                cancellationToken: cancellationToken);
        }
    }
}
