// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Pure builders for registering the KlacksStt Windows service: sc.exe argument lists, service environment, account and
/// paths. The values mirror the KlacksStt part of Klacks.Api deploy/native/windows/Install-Klacks.ps1 and must be kept
/// in sync with it. The service runs as its own virtual account (NT SERVICE\name) with an unrestricted service SID.
/// </summary>

using Klacks.Updater.Application;

namespace Klacks.Updater.Infrastructure;

public static class SttServiceRegistration
{
    public const string MissingBinaryMessage =
        "The installed Klacks version does not contain the speech recognition service; update Klacks first.";

    public const int Port = 5091;

    public const string NativeBaseUrl = "http://localhost:5091";

    public const string AllowedHosts = "localhost;127.0.0.1";

    public const string StartTypeAuto = "auto";

    public const string StartTypeDisabled = "disabled";

    private const string SttDirectoryName = "stt";
    private const string SttExecutableName = "Klacks.Stt.exe";
    private const string ModelDirectoryName = "stt-models";
    private const string DisplayName = "Klacks Speech Recognition";
    private const string ServicesRegistryRoot = @"SYSTEM\CurrentControlSet\Services";
    private const string ServiceAccountPrefix = @"NT SERVICE\";
    private const string ServiceControlExecutableName = "sc.exe";
    private const string SidTypeUnrestricted = "unrestricted";
    private const int NumThreads = 2;
    private const int FailureResetSeconds = 86400;
    private const string FailureActions = "restart/60000/restart/60000/restart/300000";
    private const string ProductionEnvironment = "Production";

    public static string ServiceControlExecutable =>
        Path.Combine(Environment.SystemDirectory, ServiceControlExecutableName);

    public static string ResolveNativeBaseUrl(string? configuredBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            return NativeBaseUrl;
        }

        var trimmed = configuredBaseUrl.Trim().TrimEnd('/');
        return string.Equals(trimmed, UpdaterOptions.DefaultSttBaseUrl, StringComparison.OrdinalIgnoreCase)
            ? NativeBaseUrl
            : configuredBaseUrl;
    }

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

    public static string ServiceAccount(string serviceName) => ServiceAccountPrefix + serviceName;

    public static string[] BuildCreateArguments(string serviceName, string executable) =>
        ["create", serviceName, "binPath=", $"\"{executable}\"", "start=", StartTypeDisabled, "DisplayName=", DisplayName];

    public static string[] BuildAccountArguments(string serviceName) =>
        ["config", serviceName, "obj=", ServiceAccount(serviceName)];

    public static string[] BuildSidTypeArguments(string serviceName) =>
        ["sidtype", serviceName, SidTypeUnrestricted];

    public static string[] BuildFailureArguments(string serviceName) =>
        ["failure", serviceName, "reset=", FailureResetSeconds.ToString(), "actions=", FailureActions];

    public static string[] BuildFailureFlagArguments(string serviceName) => ["failureflag", serviceName, "1"];

    public static string[] BuildStartTypeArguments(string serviceName, string startType) =>
        ["config", serviceName, "start=", startType];

    public static string[] BuildEnvironment(string dataRoot) =>
    [
        $"ASPNETCORE_URLS={NativeBaseUrl}",
        $"Stt__ModelDirectory={ModelDirectory(dataRoot)}",
        $"Stt__NumThreads={NumThreads}",
        $"AllowedHosts={AllowedHosts}",
        $"ASPNETCORE_ENVIRONMENT={ProductionEnvironment}",
        $"DOTNET_ENVIRONMENT={ProductionEnvironment}",
    ];

    public static bool IsFullyRegistered(string[]? currentEnvironment, string[] expectedEnvironment) =>
        currentEnvironment is not null
        && currentEnvironment.Order(StringComparer.Ordinal).SequenceEqual(expectedEnvironment.Order(StringComparer.Ordinal));
}
