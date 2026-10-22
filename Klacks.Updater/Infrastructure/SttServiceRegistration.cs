// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Pure builders for registering the KlacksStt Windows service: sc.exe arguments, service environment and paths. The
/// values mirror the KlacksStt part of Klacks.Api deploy/native/windows/Install-Klacks.ps1 and must be kept in sync with it.
/// </summary>

namespace Klacks.Updater.Infrastructure;

public static class SttServiceRegistration
{
    public const string MissingBinaryMessage =
        "The installed Klacks version does not contain the speech recognition service; update Klacks first.";

    private const string SttDirectoryName = "stt";
    private const string SttExecutableName = "Klacks.Stt.exe";
    private const string ModelDirectoryName = "stt-models";
    private const string DisplayName = "Klacks Speech Recognition";
    private const string ServicesRegistryRoot = @"SYSTEM\CurrentControlSet\Services";
    private const int Port = 5091;
    private const int NumThreads = 2;
    private const int FailureResetSeconds = 86400;
    private const string FailureActions = "restart/60000/restart/60000/restart/300000";
    private const string ProductionEnvironment = "Production";

    public static string ExecutablePath(NativeInstallLayout layout) =>
        Path.Combine(layout.CurrentLink, SttDirectoryName, SttExecutableName);

    public static string ResolveExecutable(NativeInstallLayout layout)
    {
        var executable = ExecutablePath(layout);
        if (!File.Exists(executable))
        {
            throw new InvalidOperationException(MissingBinaryMessage);
        }

        return executable;
    }

    public static string ModelDirectory(string dataRoot) => Path.Combine(dataRoot, ModelDirectoryName);

    public static string ServiceRegistryPath(string serviceName) => $@"{ServicesRegistryRoot}\{serviceName}";

    public static string BuildCreateArguments(string serviceName, string executable) =>
        $"create {serviceName} binPath= \"\\\"{executable}\\\"\" start= disabled DisplayName= \"{DisplayName}\"";

    public static string BuildFailureArguments(string serviceName) =>
        $"failure {serviceName} reset= {FailureResetSeconds} actions= {FailureActions}";

    public static string BuildFailureFlagArguments(string serviceName) => $"failureflag {serviceName} 1";

    public static string[] BuildEnvironment(string dataRoot) =>
    [
        $"ASPNETCORE_URLS=http://localhost:{Port}",
        $"Stt__ModelDirectory={ModelDirectory(dataRoot)}",
        $"Stt__NumThreads={NumThreads}",
        $"ASPNETCORE_ENVIRONMENT={ProductionEnvironment}",
        $"DOTNET_ENVIRONMENT={ProductionEnvironment}",
    ];
}
