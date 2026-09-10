// Copyright (c) Heribert Gasparoli Private. All rights reserved.

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
}
