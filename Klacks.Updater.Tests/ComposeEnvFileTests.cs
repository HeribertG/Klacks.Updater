// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Infrastructure;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class ComposeEnvFileTests
{
    private const string Whisper = "whisper";

    [Test]
    public void WithProfile_appends_entry_when_missing()
    {
        var lines = new[] { "POSTGRES_PASSWORD=secret" };

        var result = ComposeEnvFile.WithProfile(lines, Whisper);

        result.ShouldBe(["POSTGRES_PASSWORD=secret", "COMPOSE_PROFILES=whisper"]);
    }

    [Test]
    public void WithProfile_merges_into_existing_entry()
    {
        var lines = new[] { "COMPOSE_PROFILES=updater", "POSTGRES_PASSWORD=secret" };

        var result = ComposeEnvFile.WithProfile(lines, Whisper);

        result.ShouldBe(["COMPOSE_PROFILES=updater,whisper", "POSTGRES_PASSWORD=secret"]);
    }

    [Test]
    public void WithProfile_is_idempotent()
    {
        var lines = new[] { "COMPOSE_PROFILES=whisper" };

        var result = ComposeEnvFile.WithProfile(lines, Whisper);

        result.ShouldBeSameAs(lines);
    }

    [Test]
    public void WithoutProfile_removes_token_and_keeps_others()
    {
        var lines = new[] { "COMPOSE_PROFILES=updater,whisper" };

        var result = ComposeEnvFile.WithoutProfile(lines, Whisper);

        result.ShouldBe(["COMPOSE_PROFILES=updater"]);
    }

    [Test]
    public void WithoutProfile_drops_line_when_last_token_removed()
    {
        var lines = new[] { "POSTGRES_PASSWORD=secret", "COMPOSE_PROFILES=whisper" };

        var result = ComposeEnvFile.WithoutProfile(lines, Whisper);

        result.ShouldBe(["POSTGRES_PASSWORD=secret"]);
    }

    [Test]
    public void WithoutProfile_returns_same_lines_when_absent()
    {
        var lines = new[] { "COMPOSE_PROFILES=updater" };

        var result = ComposeEnvFile.WithoutProfile(lines, Whisper);

        result.ShouldBeSameAs(lines);
    }

    [Test]
    public void WithValue_appends_entry_when_key_is_missing()
    {
        var lines = new[] { "POSTGRES_PASSWORD=secret" };

        var result = ComposeEnvFile.WithValue(lines, "KLACKS_API_TAG", "1.0.27");

        result.ShouldBe(["POSTGRES_PASSWORD=secret", "KLACKS_API_TAG=1.0.27"]);
    }

    [Test]
    public void WithValue_replaces_existing_entry_in_place()
    {
        var lines = new[] { "KLACKS_API_TAG=1.0.25", "POSTGRES_PASSWORD=secret" };

        var result = ComposeEnvFile.WithValue(lines, "KLACKS_API_TAG", "1.0.27");

        result.ShouldBe(["KLACKS_API_TAG=1.0.27", "POSTGRES_PASSWORD=secret"]);
    }

    [Test]
    public void WithValue_returns_same_lines_when_value_already_set()
    {
        var lines = new[] { "KLACKS_API_TAG=1.0.27" };

        var result = ComposeEnvFile.WithValue(lines, "KLACKS_API_TAG", "1.0.27");

        result.ShouldBeSameAs(lines);
    }

    [Test]
    public void WithValue_does_not_touch_keys_that_merely_share_the_prefix()
    {
        var lines = new[] { "KLACKS_API_TAG_PREVIOUS=1.0.24", "KLACKS_API_TAG=1.0.25" };

        var result = ComposeEnvFile.WithValue(lines, "KLACKS_API_TAG", "1.0.27");

        result.ShouldBe(["KLACKS_API_TAG_PREVIOUS=1.0.24", "KLACKS_API_TAG=1.0.27"]);
    }

    [Test]
    public void GetValue_returns_value_of_existing_entry()
    {
        var lines = new[] { "POSTGRES_PASSWORD=secret", "KLACKS_STT_TAG=1.2.0" };

        ComposeEnvFile.GetValue(lines, "KLACKS_STT_TAG").ShouldBe("1.2.0");
    }

    [TestCase("KLACKS_STT_TAG=\"1.2.0\"")]
    [TestCase("KLACKS_STT_TAG='1.2.0'")]
    [TestCase("  KLACKS_STT_TAG= 1.2.0 ")]
    public void GetValue_strips_quotes_and_whitespace(string line)
    {
        ComposeEnvFile.GetValue([line], "KLACKS_STT_TAG").ShouldBe("1.2.0");
    }

    [Test]
    public void GetValue_returns_null_when_missing_or_empty_or_only_prefix_matches()
    {
        ComposeEnvFile.GetValue(["KLACKS_STT_TAG_OLD=1.0.0"], "KLACKS_STT_TAG").ShouldBeNull();
        ComposeEnvFile.GetValue(["KLACKS_STT_TAG="], "KLACKS_STT_TAG").ShouldBeNull();
        ComposeEnvFile.GetValue([], "KLACKS_STT_TAG").ShouldBeNull();
    }

    [TestCase("COMPOSE_PROFILES=parakeet", true)]
    [TestCase("COMPOSE_PROFILES=updater,Parakeet", true)]
    [TestCase("COMPOSE_PROFILES=whisper", false)]
    [TestCase("POSTGRES_PASSWORD=parakeet", false)]
    public void HasProfile_detects_profile_token(string line, bool expected)
    {
        ComposeEnvFile.HasProfile([line], "parakeet").ShouldBe(expected);
    }

    [Test]
    public void HasProfile_is_false_without_profiles_line()
    {
        ComposeEnvFile.HasProfile([], "parakeet").ShouldBeFalse();
    }
}
