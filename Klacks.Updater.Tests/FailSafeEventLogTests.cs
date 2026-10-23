// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.Runtime.Versioning;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class FailSafeEventLogTests
{
    private const string Category = "Klacks.Updater.Worker";
    private const string WriteFailureMessage = "Cannot open log for source 'Klacks.Updater'. You may not have write access.";

    private ThrowingLogger _innerLogger = null!;
    private ILoggerProvider _innerProvider = null!;
    private StringWriter _errorOutput = null!;

    [SetUp]
    public void SetUp()
    {
        _innerLogger = new ThrowingLogger();
        _innerProvider = Substitute.For<ILoggerProvider>();
        _innerProvider.CreateLogger(Arg.Any<string>()).Returns(_innerLogger);
        _errorOutput = new StringWriter();
    }

    [TearDown]
    public void TearDown()
    {
        _errorOutput.Dispose();
    }

    [Test]
    public void Wrap_ReplacesInnerProviderRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILoggerProvider, InnerProvider>();

        FailSafeEventLog.Wrap(services, typeof(InnerProvider), _ => _innerProvider, _errorOutput);

        services.Count(descriptor => descriptor.ImplementationType == typeof(InnerProvider)).ShouldBe(0);
        using var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetServices<ILoggerProvider>().OfType<FailSafeLoggerProvider>().Count().ShouldBe(1);
    }

    [Test]
    public void Wrap_WithoutInnerProviderRegistration_LeavesServicesUntouched()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var countBefore = services.Count;

        FailSafeEventLog.Wrap(services, typeof(InnerProvider), _ => _innerProvider, _errorOutput);

        services.Count.ShouldBe(countBefore);
    }

    [Test]
    public void Wrap_WhenInnerLoggerThrows_LoggingDoesNotThrow()
    {
        using var serviceProvider = BuildWrappedServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(Category);

        Should.NotThrow(() => logger.LogError("Updater failed"));

        _errorOutput.ToString().ShouldContain(WriteFailureMessage);
    }

    [Test]
    public void Wrap_ForwardsWarningAndAbove_ButNotInformation()
    {
        using var serviceProvider = BuildWrappedServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(Category);

        logger.LogInformation("Klacks updater started.");
        _innerLogger.LoggedLevels.ShouldBeEmpty();

        logger.LogWarning("Update failed");
        _innerLogger.LoggedLevels.ShouldBe(new[] { LogLevel.Warning });
    }

    [Test]
    public void Register_WithoutEventLogProviderRegistration_LeavesServicesUntouched()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var countBefore = services.Count;

        FailSafeEventLog.Register(services);

        services.Count.ShouldBe(countBefore);
    }

    [Test]
    public void Wrap_ReplacesDescriptorRegisteredLikeTheHostDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, InnerProvider>());

        FailSafeEventLog.Wrap(services, typeof(InnerProvider), _ => _innerProvider, _errorOutput);

        services.Count(descriptor => descriptor.ImplementationType == typeof(InnerProvider)).ShouldBe(0);
        using var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetServices<ILoggerProvider>().OfType<FailSafeLoggerProvider>().Count().ShouldBe(1);
    }

    [Test]
    public void Wrap_ReplacesTheDescriptorOfTheRealAddEventLog()
    {
        var services = new ServiceCollection();
#pragma warning disable CA1416
        services.AddLogging(builder => builder.AddEventLog());
#pragma warning restore CA1416
        services.Count(descriptor => descriptor.ImplementationType == typeof(EventLogLoggerProvider)).ShouldBe(1);

        FailSafeEventLog.Wrap(services, typeof(EventLogLoggerProvider), _ => _innerProvider, _errorOutput);

        services.Count(descriptor => descriptor.ImplementationType == typeof(EventLogLoggerProvider)).ShouldBe(0);
        services.Count(IsLoggerProviderFactory).ShouldBe(1);
    }

    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void Register_OnTheHostBuilderOfAWindowsHost_ReplacesTheBareEventLogProvider()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddWindowsService(options => options.ServiceName = "Klacks.Updater");
        builder.Services.Count(descriptor => descriptor.ImplementationType == typeof(EventLogLoggerProvider)).ShouldBe(1);

        FailSafeEventLog.Register(builder.Services);

        builder.Services.Count(descriptor => descriptor.ImplementationType == typeof(EventLogLoggerProvider)).ShouldBe(0);
        var factoryDescriptors = builder.Services.Where(IsLoggerProviderFactory).ToList();
        factoryDescriptors.Count.ShouldBe(1);
        using var serviceProvider = builder.Services.BuildServiceProvider();
        using var provider = (IDisposable)factoryDescriptors[0].ImplementationFactory!(serviceProvider);
        provider.ShouldBeOfType<FailSafeLoggerProvider>();
    }

    private static bool IsLoggerProviderFactory(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType == typeof(ILoggerProvider) && descriptor.ImplementationFactory != null;
    }

    private ServiceProvider BuildWrappedServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ILoggerProvider, InnerProvider>();
        FailSafeEventLog.Wrap(services, typeof(InnerProvider), _ => _innerProvider, _errorOutput);
        return services.BuildServiceProvider();
    }

    private sealed class ThrowingLogger : ILogger
    {
        public List<LogLevel> LoggedLevels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            LoggedLevels.Add(logLevel);
            throw new InvalidOperationException(WriteFailureMessage);
        }
    }

    private sealed class InnerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }
}
