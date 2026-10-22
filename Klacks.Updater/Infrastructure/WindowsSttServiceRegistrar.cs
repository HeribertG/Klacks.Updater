// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Registers the KlacksStt Windows service (disabled, own virtual account, failure actions, protected model directory,
/// environment) and converges a half-configured or outdated registration: the service is created only when missing, every
/// other step is idempotent and re-applied, and the Environment value is written last as the "fully registered" marker.
/// Mirrors the KlacksStt part of Klacks.Api deploy/native/windows/Install-Klacks.ps1; the values come from
/// SttServiceRegistration.
/// </summary>
/// <param name="options">Updater configuration; NativeProgramRoot locates the current link, NativeSttServiceName names the service</param>
/// <param name="configuration">Host configuration; Hosting:DataRoot is the native data root that holds stt-models</param>
/// <param name="logger">Logger for diagnostic output</param>

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
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
        var dataRoot = ResolveDataRoot();
        var expectedEnvironment = SttServiceRegistration.BuildEnvironment(dataRoot);
        var exists = WindowsServices.Exists(serviceName);
        if (exists && SttServiceRegistration.IsFullyRegistered(ReadEnvironment(serviceName), expectedEnvironment))
        {
            return;
        }

        var executable = SttServiceRegistration.ResolveExecutable(new NativeInstallLayout(_options.NativeProgramRoot));
        var modelDirectory = SttServiceRegistration.ModelDirectory(dataRoot);

        _logger.LogInformation("Registering native STT service {Service} (disabled).", serviceName);

        ApplyModelDirectoryAcl(modelDirectory, new SecurityIdentifier(ServiceSid.FromName(serviceName)));

        if (!exists)
        {
            await RunServiceControlAsync(SttServiceRegistration.BuildCreateArguments(serviceName, executable), cancellationToken);
        }

        await RunServiceControlAsync(SttServiceRegistration.BuildSidTypeArguments(serviceName), cancellationToken);
        await RunServiceControlAsync(SttServiceRegistration.BuildAccountArguments(serviceName), cancellationToken);
        await RunServiceControlAsync(SttServiceRegistration.BuildFailureArguments(serviceName), cancellationToken);
        await RunServiceControlAsync(SttServiceRegistration.BuildFailureFlagArguments(serviceName), cancellationToken);

        WriteEnvironment(serviceName, expectedEnvironment);
    }

    private static Task RunServiceControlAsync(string[] arguments, CancellationToken cancellationToken) =>
        ProcessRunner.RunAsync(SttServiceRegistration.ServiceControlExecutable, arguments, cancellationToken: cancellationToken);

    private static string[]? ReadEnvironment(string serviceName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(SttServiceRegistration.ServiceRegistryPath(serviceName));
        return key?.GetValue(EnvironmentValueName) as string[];
    }

    private static void ApplyModelDirectoryAcl(string path, SecurityIdentifier serviceSid)
    {
        var directory = Directory.CreateDirectory(path);

        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sidType in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(CreateRule(new SecurityIdentifier(sidType, null), FileSystemRights.FullControl));
        }

        security.AddAccessRule(CreateRule(serviceSid, FileSystemRights.Modify));

        directory.SetAccessControl(security);
    }

    private static FileSystemAccessRule CreateRule(SecurityIdentifier identity, FileSystemRights rights) =>
        new(
            identity,
            rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow);

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
