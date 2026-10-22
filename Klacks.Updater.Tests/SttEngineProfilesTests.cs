// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class SttEngineProfilesTests
{
    private readonly UpdaterOptions _options = new()
    {
        WhisperServiceName = "whisper-x",
        WhisperComposeProfile = "whisper-p",
        WhisperBaseUrl = "http://whisper-x:8000",
        SttServiceName = "stt-x",
        SttComposeProfile = "stt-p",
        SttBaseUrl = "http://stt-x:8000",
    };

    [Test]
    public void Resolve_small_returns_whisper_profile_with_small_requirements()
    {
        var profile = SttEngineProfiles.Resolve(WhisperPluginConstants.ModelAliasSmall, _options);

        profile.ShouldNotBeNull();
        profile.IsParakeet.ShouldBeFalse();
        profile.ServiceName.ShouldBe("whisper-x");
        profile.ComposeProfile.ShouldBe("whisper-p");
        profile.BaseUrl.ShouldBe("http://whisper-x:8000");
        profile.ProviderName.ShouldBe(WhisperPluginConstants.ProviderName);
        profile.RequiredMemoryBytes.ShouldBe(WhisperPluginConstants.SmallModelRequiredMemoryBytes);
        profile.RequiredDiskBytes.ShouldBe(WhisperPluginConstants.SmallModelRequiredDiskBytes);
    }

    [Test]
    public void Resolve_large_returns_whisper_profile_with_large_requirements()
    {
        var profile = SttEngineProfiles.Resolve(WhisperPluginConstants.ModelAliasLarge, _options);

        profile.ShouldNotBeNull();
        profile.IsParakeet.ShouldBeFalse();
        profile.ServiceName.ShouldBe("whisper-x");
        profile.RequiredMemoryBytes.ShouldBe(WhisperPluginConstants.LargeModelRequiredMemoryBytes);
        profile.RequiredDiskBytes.ShouldBe(WhisperPluginConstants.LargeModelRequiredDiskBytes);
    }

    [Test]
    public void Resolve_parakeet_returns_stt_profile()
    {
        var profile = SttEngineProfiles.Resolve(WhisperPluginConstants.ModelAliasParakeet, _options);

        profile.ShouldNotBeNull();
        profile.IsParakeet.ShouldBeTrue();
        profile.Alias.ShouldBe(WhisperPluginConstants.ModelAliasParakeet);
        profile.ServiceName.ShouldBe("stt-x");
        profile.ComposeProfile.ShouldBe("stt-p");
        profile.BaseUrl.ShouldBe("http://stt-x:8000");
        profile.ProviderName.ShouldBe(WhisperPluginConstants.ParakeetProviderName);
        profile.RequiredMemoryBytes.ShouldBe(WhisperPluginConstants.ParakeetRequiredMemoryBytes);
        profile.RequiredDiskBytes.ShouldBe(WhisperPluginConstants.ParakeetRequiredDiskBytes);
    }

    [TestCase("unknown")]
    [TestCase("")]
    [TestCase(null)]
    public void Resolve_unknown_alias_returns_null(string? alias)
    {
        SttEngineProfiles.Resolve(alias!, _options).ShouldBeNull();
    }

    [Test]
    public void All_returns_one_whisper_and_one_parakeet_profile_with_distinct_services()
    {
        var all = SttEngineProfiles.All(_options);

        all.Count.ShouldBe(2);
        all.Select(p => p.ServiceName).Distinct().Count().ShouldBe(2);
        all.Count(p => p.IsParakeet).ShouldBe(1);
    }

    [Test]
    public void Default_options_use_documented_stt_values()
    {
        var defaults = new UpdaterOptions();

        defaults.SttServiceName.ShouldBe("klacks-stt");
        defaults.SttComposeProfile.ShouldBe("parakeet");
        defaults.SttBaseUrl.ShouldBe("http://klacks-stt:8000");
        defaults.NativeSttServiceName.ShouldBe("KlacksStt");
    }
}
