// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using Klacks.Updater;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

var nativeDataRoot = builder.Configuration[UpdaterConstants.NativeDataRootKey];
if (!string.IsNullOrWhiteSpace(nativeDataRoot))
{
    builder.Configuration.AddJsonFile(
        Path.Combine(nativeDataRoot, UpdaterConstants.NativeConfigDirectoryName, UpdaterConstants.NativeConfigFileName),
        optional: false,
        reloadOnChange: false);
}

builder.Services.Configure<UpdaterOptions>(builder.Configuration.GetSection(UpdaterOptions.SectionName));

builder.Services.AddHttpClient(HttpHealthGate.HttpClientName);
builder.Services.AddHttpClient(HttpWhisperModelInstaller.HttpClientName, client =>
{
    client.Timeout = UpdaterConstants.WhisperModelInstallTimeout;
});

builder.Services.AddSingleton<IUpdateOperationStore, NpgsqlUpdateOperationStore>();
var mode = builder.Configuration.GetSection(UpdaterOptions.SectionName).GetValue<UpdaterMode>(nameof(UpdaterOptions.Mode));
if (mode == UpdaterMode.Native)
{
    builder.Services.AddWindowsService(options => options.ServiceName = UpdaterConstants.NativeUpdaterServiceName);
    builder.Services.AddHttpClient(NativeBundleApplier.HttpClientName, client =>
    {
        client.Timeout = UpdaterConstants.BundleDownloadTimeout;
    });
    builder.Services.AddSingleton<IUpdateApplier, NativeBundleApplier>();
    builder.Services.AddSingleton<IUpdateBackupService, NativePgDumpBackupService>();
    builder.Services.PostConfigure<UpdaterOptions>(options =>
        options.SttBaseUrl = SttServiceRegistration.ResolveNativeBaseUrl(options.SttBaseUrl));
    if (OperatingSystem.IsWindows())
    {
        builder.Services.AddSingleton<ISttServiceLifecycle, WindowsSttServiceLifecycle>();
        builder.Services.AddSingleton<ISttServiceRegistrar, WindowsSttServiceRegistrar>();
        builder.Services.AddSingleton<IWhisperPluginApplier, WindowsSttServiceApplier>();
        builder.Services.AddSingleton<IServiceController, WindowsServiceController>();
        builder.Services.AddSingleton<IVersionLinkSwitcher, JunctionLinkSwitcher>();
    }
    else
    {
        builder.Services.AddSingleton<ISttServiceLifecycle, NoOpSttServiceLifecycle>();
        builder.Services.AddSingleton<IWhisperPluginApplier, UnsupportedWhisperPluginApplier>();
        builder.Services.AddSingleton<IServiceController, LaunchdServiceController>();
        builder.Services.AddSingleton<IVersionLinkSwitcher, SymbolicLinkSwitcher>();
    }
}
else
{
    builder.Services.AddSingleton<IProcessRunner, SystemProcessRunner>();
    builder.Services.AddSingleton<ISttTagSynchronizer, DockerSttTagSynchronizer>();
    builder.Services.AddSingleton<IUpdateApplier, DockerApplier>();
    builder.Services.AddSingleton<IUpdateBackupService, PgDumpBackupService>();
    builder.Services.AddSingleton<IWhisperPluginApplier, DockerWhisperApplier>();
}

builder.Services.AddSingleton<IUpdateHealthGate, HttpHealthGate>();
builder.Services.AddSingleton<IArtifactVerifier, RsaArtifactVerifier>();
builder.Services.AddSingleton<IBackupRetentionService, FileBackupRetentionService>();
builder.Services.AddSingleton<IWhisperModelInstaller, HttpWhisperModelInstaller>();
builder.Services.AddSingleton<IWhisperProviderStore, NpgsqlWhisperProviderStore>();
if (mode == UpdaterMode.Native && OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<IWhisperResourceCheck, WindowsWhisperResourceCheck>();
}
else
{
    builder.Services.AddSingleton<IWhisperResourceCheck, LinuxWhisperResourceCheck>();
}

builder.Services.AddSingleton<UpdateExecutor>();
builder.Services.AddSingleton<WhisperPluginExecutor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
