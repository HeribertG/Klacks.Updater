// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class SignedFieldFormatTests
{
    private static UpdateOperation App(string target, string? artifactRef = "ghcr.io/heribertg/klacks-api:1.0.79") => new()
    {
        Id = Guid.NewGuid(),
        OperationType = UpdateOperationType.Update,
        TargetVersion = target,
        ArtifactRef = artifactRef,
        ArtifactSha256 = "abc",
    };

    [TestCase("1.0.79")]
    [TestCase("1.0.80-rc1")]
    [TestCase("1.0.80-rc.1")]
    [TestCase("2.10.3-beta-2")]
    public void Release_versions_in_use_stay_valid(string target) =>
        SignedFieldFormat.IsValid(App(target)).ShouldBeTrue();

    [TestCase("klacks-postgres-engine\n19")]
    [TestCase("1.0.79\n")]
    [TestCase("1.0")]
    [TestCase("19")]
    [TestCase("v1.0.79")]
    [TestCase("1.0.79 ")]
    [TestCase("")]
    public void Application_update_with_a_non_release_target_is_rejected(string target) =>
        SignedFieldFormat.IsValid(App(target)).ShouldBeFalse();

    [Test]
    public void Control_characters_in_the_artifact_reference_are_rejected()
    {
        SignedFieldFormat.IsValid(App("1.0.79", "ghcr.io/x:1\n19")).ShouldBeFalse();
        SignedFieldFormat.IsValid(App("1.0.79", "ghcr.io/x:1\u0000")).ShouldBeFalse();
    }

    [TestCase("17", "19", true)]
    [TestCase("9", "100", true)]
    [TestCase("17", "1.0.79", false)]
    [TestCase("17", "1000", false)]
    [TestCase("", "19", false)]
    [TestCase("17", "19\n", false)]
    [TestCase("17", "١٩", false)]
    public void Engine_upgrade_needs_short_major_numbers(string from, string to, bool valid) =>
        SignedFieldFormat.IsValid(EngineUpgradeHarness.Operation(from, to)).ShouldBe(valid);

    [Test]
    public void Rollback_rows_need_a_release_target_too() =>
        SignedFieldFormat.IsValid(App("klacks") with { OperationType = UpdateOperationType.Rollback }).ShouldBeFalse();

    [Test]
    public void Speech_to_text_rows_keep_their_model_alias() =>
        SignedFieldFormat.IsValid(App("large-v3-turbo") with { OperationType = UpdateOperationType.WhisperInstall }).ShouldBeTrue();

    [Test]
    public void Local_speech_install_rows_keep_their_model_alias() =>
        SignedFieldFormat.IsValid(App("supertonic-3", "supertonic-3-int8") with { OperationType = UpdateOperationType.TtsInstall }).ShouldBeTrue();

    [Test]
    public void Local_speech_install_rows_with_control_characters_are_rejected()
    {
        var row = App("supertonic-3", "supertonic-3-int8") with { OperationType = UpdateOperationType.TtsInstall };

        SignedFieldFormat.IsValid(row with { TargetVersion = "supertonic-3\n19" }).ShouldBeFalse();
        SignedFieldFormat.IsValid(row with { ArtifactRef = "supertonic-3-int8\n" }).ShouldBeFalse();
    }

    [Test]
    public void Verifier_rejects_a_crafted_app_row_before_any_signature_check_even_in_unsigned_mode()
    {
        var verifier = new RsaArtifactVerifier(Options.Create(new UpdaterOptions()), NullLogger<RsaArtifactVerifier>.Instance);

        verifier.Verify(App("klacks-postgres-engine\n19")).ShouldBeFalse();
        verifier.Verify(App("1.0.79")).ShouldBeTrue();
    }
}
