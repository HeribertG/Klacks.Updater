// Copyright (c) Heribert Gasparoli Private. All rights reserved.

using Klacks.Updater;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<UpdaterOptions>(builder.Configuration.GetSection(UpdaterOptions.SectionName));

builder.Services.AddHttpClient(HttpHealthGate.HttpClientName);
builder.Services.AddHttpClient(HttpWhisperModelInstaller.HttpClientName, client =>
{
    client.Timeout = UpdaterConstants.WhisperModelInstallTimeout;
});

builder.Services.AddSingleton<IUpdateOperationStore, NpgsqlUpdateOperationStore>();
builder.Services.AddSingleton<IUpdateApplier, DockerApplier>();
builder.Services.AddSingleton<IUpdateBackupService, PgDumpBackupService>();
builder.Services.AddSingleton<IUpdateHealthGate, HttpHealthGate>();
builder.Services.AddSingleton<IArtifactVerifier, RsaArtifactVerifier>();
builder.Services.AddSingleton<IBackupRetentionService, FileBackupRetentionService>();
builder.Services.AddSingleton<IWhisperPluginApplier, DockerWhisperApplier>();
builder.Services.AddSingleton<IWhisperModelInstaller, HttpWhisperModelInstaller>();
builder.Services.AddSingleton<IWhisperProviderStore, NpgsqlWhisperProviderStore>();
builder.Services.AddSingleton<IWhisperResourceCheck, LinuxWhisperResourceCheck>();
builder.Services.AddSingleton<UpdateExecutor>();
builder.Services.AddSingleton<WhisperPluginExecutor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
