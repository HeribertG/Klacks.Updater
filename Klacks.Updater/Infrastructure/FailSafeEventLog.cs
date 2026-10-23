// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Replaces the bare Windows event log provider with the same provider wrapped in FailSafeLoggerProvider, so that a
/// failing event log write (for example "Cannot open log for source", Win32 error 5) never reaches the code that logged.
/// The provider is registered primarily by the host default (Host.CreateApplicationBuilder adds it on Windows);
/// AddWindowsService only configures the event log settings and adds the same provider idempotently. Register must
/// therefore run after every AddEventLog/AddWindowsService call - the ordering is load-bearing, a provider registered
/// later would stay bare. The Warning-and-above default filter is re-registered for the wrapper type, because the host
/// default is keyed to EventLogLoggerProvider and would no longer match. Without a registered event log provider
/// nothing changes.
/// </summary>

using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure;

public static class FailSafeEventLog
{
    public static void Register(IServiceCollection services)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Wrap(services, typeof(EventLogLoggerProvider), CreateEventLogProvider, Console.Error);
    }

    public static void Wrap(
        IServiceCollection services,
        Type innerProviderType,
        Func<IServiceProvider, ILoggerProvider> innerFactory,
        TextWriter errorOutput)
    {
        var innerDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(ILoggerProvider)
                && descriptor.ImplementationType == innerProviderType)
            .ToList();
        if (innerDescriptors.Count == 0)
        {
            return;
        }

        foreach (var descriptor in innerDescriptors)
        {
            services.Remove(descriptor);
        }

        services.AddSingleton<ILoggerProvider>(serviceProvider =>
            new FailSafeLoggerProvider(innerFactory(serviceProvider), errorOutput));
        services.AddLogging(builder =>
            builder.AddFilter<FailSafeLoggerProvider>(level => level >= LogLevel.Warning));
    }

    [SupportedOSPlatform("windows")]
    private static ILoggerProvider CreateEventLogProvider(IServiceProvider serviceProvider)
    {
        return new EventLogLoggerProvider(serviceProvider.GetRequiredService<IOptions<EventLogSettings>>());
    }
}
