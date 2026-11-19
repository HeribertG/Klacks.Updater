// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class EngineUpgradePrecheckTests
{
    private const long GigaByte = 1024L * 1024 * 1024;

    private static readonly EngineInspection Supported = new(17, new Dictionary<string, string>(), 100 * GigaByte, null);

    private static readonly EngineClusterFacts Facts = new(
        17,
        [new EngineDatabase("Klacks", "admin", "UTF8", "C", "C", "libc", null)],
        [],
        GigaByte);

    private static string? Evaluate(UpdateOperation operation, EngineInspection? inspection = null, EngineClusterFacts? facts = null, string? version = "1.0.79") =>
        EngineUpgradePrecheck.Evaluate(operation, inspection ?? Supported, facts ?? Facts, version);

    [Test]
    public void Valid_request_passes() => Evaluate(EngineUpgradeHarness.Operation()).ShouldBeNull();

    [TestCase("17", "17")]
    [TestCase("19", "17")]
    [TestCase("17.2", "19")]
    [TestCase("", "19")]
    [TestCase("17", "nineteen")]
    public void Majors_must_be_numbers_and_increase(string from, string to) =>
        Evaluate(EngineUpgradeHarness.Operation(from, to)).ShouldNotBeNull();

    [Test]
    public void Server_major_must_match_from_version() =>
        Evaluate(EngineUpgradeHarness.Operation(), facts: Facts with { ServerMajor = 16 }).ShouldNotBeNull();

    [Test]
    public void Deployment_view_must_match_from_version() =>
        Evaluate(EngineUpgradeHarness.Operation(), inspection: Supported with { CurrentMajor = 19 }).ShouldNotBeNull();

    [Test]
    public void Unsupported_deployment_is_rejected_with_its_reason() =>
        Evaluate(EngineUpgradeHarness.Operation(), inspection: Supported with { UnsupportedReason = "compose not parametrised" })
            .ShouldBe("compose not parametrised");

    [Test]
    public void Foreign_roles_are_rejected() =>
        Evaluate(EngineUpgradeHarness.Operation(), facts: Facts with { ForeignRoles = ["reporting"] })!.ShouldContain("reporting");

    [Test]
    public void Free_space_must_be_three_times_the_databases_and_at_least_two_gigabytes()
    {
        EngineUpgradePrecheck.RequiredFreeBytes(GigaByte / 2).ShouldBe(2 * GigaByte);
        EngineUpgradePrecheck.RequiredFreeBytes(10 * GigaByte).ShouldBe(30 * GigaByte);
        Evaluate(EngineUpgradeHarness.Operation(), inspection: Supported with { FreeBytes = 3 * GigaByte - 1 }, facts: Facts with { TotalDatabaseBytes = GigaByte })
            .ShouldNotBeNull();
        Evaluate(EngineUpgradeHarness.Operation(), inspection: Supported with { FreeBytes = 3 * GigaByte }, facts: Facts with { TotalDatabaseBytes = GigaByte })
            .ShouldBeNull();
    }

    [TestCase("sha256:abc")]
    [TestCase("SHA256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [TestCase("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [TestCase("")]
    [TestCase("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n")]
    public void Malformed_digest_is_rejected(string sha) =>
        Evaluate(EngineUpgradeHarness.Operation() with { ArtifactSha256 = sha }).ShouldNotBeNull();

    [Test]
    public void Native_hex_digest_is_accepted() =>
        Evaluate(EngineUpgradeHarness.Operation() with { ArtifactSha256 = new string('0', 64) }).ShouldBeNull();

    [Test]
    public void Unreadable_application_version_is_rejected() =>
        Evaluate(EngineUpgradeHarness.Operation(), version: null).ShouldNotBeNull();
}
