// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.Runtime.Versioning;
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
[Platform("Win")]
[SupportedOSPlatform("windows")]
public class WindowsSttServiceApplierTests
{
    private readonly UpdaterOptions _options = new();

    private readonly ISttServiceRegistrar _registrar = Substitute.For<ISttServiceRegistrar>();

    private WindowsSttServiceApplier CreateApplier() =>
        new(Options.Create(_options), _registrar, NullLogger<WindowsSttServiceApplier>.Instance);

    [Test]
    public void Supports_Parakeet_ReturnsTrue()
    {
        var profile = SttEngineProfiles.Resolve(WhisperPluginConstants.ModelAliasParakeet, _options)!;

        CreateApplier().Supports(profile).ShouldBeTrue();
    }

    [TestCase(WhisperPluginConstants.ModelAliasSmall)]
    [TestCase(WhisperPluginConstants.ModelAliasLarge)]
    public void Supports_Whisper_ReturnsFalse(string alias)
    {
        var profile = SttEngineProfiles.Resolve(alias, _options)!;

        CreateApplier().Supports(profile).ShouldBeFalse();
    }

    [Test]
    public async Task Start_registration_failure_propagates_before_the_service_is_touched()
    {
        var profile = SttEngineProfiles.Resolve(WhisperPluginConstants.ModelAliasParakeet, _options)!;
        _registrar.EnsureRegisteredAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException(SttServiceRegistration.MissingBinaryMessage)));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => CreateApplier().StartAsync(profile));

        exception.Message.ShouldBe(SttServiceRegistration.MissingBinaryMessage);
    }
}
