// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Infrastructure;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class SttServiceRegistrationTests
{
    private const string ServiceName = "KlacksStt";
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "klacks-stt-reg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void ExecutablePath_points_into_the_current_link()
    {
        var layout = new NativeInstallLayout(_root);

        SttServiceRegistration.ExecutablePath(layout)
            .ShouldBe(Path.Combine(_root, "current", "stt", "Klacks.Stt.exe"));
    }

    [Test]
    public void ResolveExecutable_throws_when_the_installed_version_has_no_stt_binary()
    {
        var layout = new NativeInstallLayout(_root);

        var exception = Should.Throw<InvalidOperationException>(() => SttServiceRegistration.ResolveExecutable(layout));

        exception.Message.ShouldBe(
            "The installed Klacks version does not contain the speech recognition service; update Klacks first.");
    }

    [Test]
    public void ResolveExecutable_returns_the_path_when_the_binary_exists()
    {
        var layout = new NativeInstallLayout(_root);
        var expected = SttServiceRegistration.ExecutablePath(layout);
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        File.WriteAllText(expected, string.Empty);

        SttServiceRegistration.ResolveExecutable(layout).ShouldBe(expected);
    }

    [Test]
    public void BuildCreateArguments_registers_the_service_disabled_with_the_quoted_binary()
    {
        var arguments = SttServiceRegistration.BuildCreateArguments(ServiceName, @"C:\Program Files\Klacks\current\stt\Klacks.Stt.exe");

        arguments.ShouldBe(
            "create KlacksStt binPath= \"\\\"C:\\Program Files\\Klacks\\current\\stt\\Klacks.Stt.exe\\\"\" "
            + "start= disabled DisplayName= \"Klacks Speech Recognition\"");
    }

    [Test]
    public void BuildFailureArguments_restarts_after_crashes_like_the_installer()
    {
        SttServiceRegistration.BuildFailureArguments(ServiceName)
            .ShouldBe("failure KlacksStt reset= 86400 actions= restart/60000/restart/60000/restart/300000");
    }

    [Test]
    public void BuildFailureFlagArguments_also_recovers_on_error_exit_codes()
    {
        SttServiceRegistration.BuildFailureFlagArguments(ServiceName).ShouldBe("failureflag KlacksStt 1");
    }

    [Test]
    public void ModelDirectory_is_stt_models_below_the_data_root()
    {
        SttServiceRegistration.ModelDirectory(_root).ShouldBe(Path.Combine(_root, "stt-models"));
    }

    [Test]
    public void BuildEnvironment_matches_the_installer_values()
    {
        var environment = SttServiceRegistration.BuildEnvironment(_root);

        environment.ShouldBe(new[]
        {
            "ASPNETCORE_URLS=http://localhost:5091",
            "Stt__ModelDirectory=" + Path.Combine(_root, "stt-models"),
            "Stt__NumThreads=2",
            "ASPNETCORE_ENVIRONMENT=Production",
            "DOTNET_ENVIRONMENT=Production",
        });
    }

    [Test]
    public void ServiceRegistryPath_addresses_the_service_key()
    {
        SttServiceRegistration.ServiceRegistryPath(ServiceName)
            .ShouldBe(@"SYSTEM\CurrentControlSet\Services\KlacksStt");
    }
}
