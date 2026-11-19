// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.Text;
using System.Text.Json;
using Klacks.Updater.Application;
using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class EngineUpgradeSignatureAndReportTests
{
    [Test]
    public void Engine_signature_payload_is_byte_exact()
    {
        var operation = EngineUpgradeHarness.Operation() with { ArtifactSha256 = "sha256:" + new string('b', 64) };

        var payload = Encoding.UTF8.GetBytes(ArtifactSignaturePayload.Build(operation));

        payload.ShouldBe(Encoding.UTF8.GetBytes(
            "klacks-postgres-engine\n19\nghcr.io/heribertg/klacks-postgres:19\nsha256:" + new string('b', 64)));
    }

    [Test]
    public void Engine_upgrade_without_public_key_is_rejected_even_though_app_updates_run_unsigned()
    {
        var verifier = new RsaArtifactVerifier(Options.Create(new UpdaterOptions()), NullLogger<RsaArtifactVerifier>.Instance);
        var engine = EngineUpgradeHarness.Operation() with { ArtifactSignature = "anything" };

        verifier.Verify(engine).ShouldBeFalse();
        verifier.Verify(engine with { OperationType = UpdateOperationType.Update }).ShouldBeTrue();
    }

    [Test]
    public void Signed_fields_with_line_breaks_are_always_rejected()
    {
        var verifier = new RsaArtifactVerifier(Options.Create(new UpdaterOptions()), NullLogger<RsaArtifactVerifier>.Instance);

        verifier.Verify(EngineUpgradeHarness.Operation() with { OperationType = UpdateOperationType.Update, TargetVersion = "klacks-postgres-engine\n19" })
            .ShouldBeFalse();
    }

    [Test]
    public void Allow_unsigned_engine_upgrade_lifts_the_rule_for_tests()
    {
        var verifier = new RsaArtifactVerifier(
            Options.Create(new UpdaterOptions { AllowUnsignedEngineUpgrade = true }),
            NullLogger<RsaArtifactVerifier>.Instance);

        verifier.Verify(EngineUpgradeHarness.Operation()).ShouldBeTrue();
    }

    [Test]
    public void Capability_report_has_the_frozen_shape()
    {
        var json = UpdaterCapabilityReport.Build(
            "1.0.79",
            UpdaterMode.Native,
            [EngineUpgradeConstants.EngineUpgradeCapability],
            new DateTime(2026, 11, 20, 1, 2, 3, DateTimeKind.Utc));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("updaterVersion").GetString().ShouldBe("1.0.79");
        root.GetProperty("mode").GetString().ShouldBe("Native");
        root.GetProperty("capabilities")[0].GetString().ShouldBe("postgres-engine-upgrade/1");
        root.GetProperty("reportedAtUtc").GetString().ShouldBe("2026-11-20T01:02:03.0000000Z");
        EngineUpgradeConstants.CapabilitySettingType.ShouldBe("UPDATER_CAPABILITIES");
    }
}
