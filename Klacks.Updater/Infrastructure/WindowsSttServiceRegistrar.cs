// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Registers the KlacksStt Windows service (disabled, environment, failure actions, protected model directory) on
/// installations that were created before the installer shipped it. Mirrors the KlacksStt part of Klacks.Api
/// deploy/native/windows/Install-Klacks.ps1; the values come from SttServiceRegistration.
/// </summary>
/// <param name="options">Updater configuration; NativeProgramRoot locates the current link, NativeSttServiceName names the service</param>
/// <param name="configuration">Host configuration; Hosting:DataRoot is the native data root that holds stt-models</param>
/// <param name="logger">Logger for diagnostic output</param>

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

namespace Klacks.Updater.Infrastructure;

[SupportedOSPlatform("windows")]
public class WindowsSttServiceRegistrar : ISttServiceRegistrar
{
    private const string ServiceControlExecutable = "sc.exe";
    private const string EnvironmentValueName = "Environment";

    private readonly UpdaterOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WindowsSttServiceRegistrar> _logger;

    public WindowsSttServiceRegistrar(
        IOptions<UpdaterOptions> options,
        IConfiguration configuration,
        ILogger<WindowsSttServiceRegistrar> logger)
    {
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task EnsureRegisteredAsync(CancellationToken cancellationToken = default)
    {
        var serviceName = _options.NativeSttServiceName;
        if (ServiceExists(serviceName))
        {
            return;
        }

        var executable = SttServiceRegistration.ResolveExecutable(new NativeInstallLayout(_options.NativeProgramRoot));
        var dataRoot = ResolveDataRoot();

        _logger.LogInformation("Registering native STT service {Service} (disabled).", serviceName);

        CreateProtectedModelDirectory(SttServiceRegistration.ModelDirectory(dataRoot));

        await ProcessRunner.RunAsync(
            ServiceControlExecutable,
            SttServiceRegistration.BuildCreateArguments(serviceName, executable),
            cancellationToken: cancellationToken);

        WriteEnvironment(serviceName, SttServiceRegistration.BuildEnvironment(dataRoot));

        await ProcessRunner.RunAsync(
            ServiceControlExecutable,
            SttServiceRegistration.BuildFailureArguments(serviceName),
            cancellationToken: cancellationToken);
        await ProcessRunner.RunAsync(
            ServiceControlExecutable,
            SttServiceRegistration.BuildFailureFlagArguments(serviceName),
            cancellationToken: cancellationToken);
    }

    private static bool ServiceExists(string serviceName) =>
        ServiceController.GetServices()
            .Any(service => string.Equals(service.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));

    private static void CreateProtectedModelDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sidType in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sidType, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        directory.SetAccessControl(security);
    }

    private static void WriteEnvironment(string serviceName, string[] environment)
    {
        using var key = Registry.LocalMachine.OpenSubKey(SttServiceRegistration.ServiceRegistryPath(serviceName), writable: true)
            ?? throw new InvalidOperationException($"Registry key of service {serviceName} was not found after registration.");
        key.SetValue(EnvironmentValueName, environment, RegistryValueKind.MultiString);
    }

    private string ResolveDataRoot()
    {
        var dataRoot = _configuration[UpdaterConstants.NativeDataRootKey];
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new InvalidOperationException($"{UpdaterConstants.NativeDataRootKey} must be configured in native mode.");
        }

        return dataRoot;
    }
}
