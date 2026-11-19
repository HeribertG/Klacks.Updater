// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

// Mirrored by Klacks.UnitTest/Architecture/UpdaterContractFreezeTests — change both deliberately.
namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class SharedContractFreezeTests
{
    [Test]
    public void OperationTypeValues_AreFrozen()
    {
        ((int)UpdateOperationType.Update).ShouldBe(0);
        ((int)UpdateOperationType.Rollback).ShouldBe(1);
        ((int)UpdateOperationType.WhisperInstall).ShouldBe(2);
        ((int)UpdateOperationType.WhisperUninstall).ShouldBe(3);
        ((int)UpdateOperationType.PostgresUpgrade).ShouldBe(4);
    }

    [Test]
    public void OperationTypeMembers_AreFrozen()
    {
        Enum.GetNames<UpdateOperationType>().ShouldBe(
            ["Update", "Rollback", "WhisperInstall", "WhisperUninstall", "PostgresUpgrade"],
            ignoreOrder: true);
    }

    [Test]
    public void EngineUpgradePhases_AreFrozenInOrder()
    {
        Enum.GetNames<EngineUpgradePhase>().ShouldBe(
        [
            "Accepted", "ArtifactReady", "ApplicationStopped", "SnapshotTaken", "DumpCreated", "OldEngineStopped",
            "NewEngineActive", "Restored", "Verified", "ConfigurationCommitted", "ApplicationHealthy", "Committed",
            "RolledBack", "RollbackFailed",
        ]);
        Enum.GetValues<EngineUpgradePhase>().Select(phase => (int)phase).ShouldBe(Enumerable.Range(0, 14));
    }

    [Test]
    public void EngineUpgradeOptionDefaults_AreFrozen()
    {
        var defaults = new UpdaterOptions();

        defaults.PostgresServiceName.ShouldBe("postgres");
        defaults.PostgresImageRepository.ShouldBe("ghcr.io/heribertg/klacks-postgres");
        defaults.PostgresImageEnvVar.ShouldBe("KLACKS_POSTGRES_IMAGE");
        defaults.PostgresDataSubdirEnvVar.ShouldBe("KLACKS_POSTGRES_DATA_SUBDIR");
        defaults.NativePostgresServiceName.ShouldBe("KlacksPostgres");
        defaults.AllowUnsignedEngineUpgrade.ShouldBeFalse();
        defaults.EngineUpgradeFaultPhase.ShouldBeNull();
        UpdaterConstants.DockerEngineUpgradeExchangeDirectory.ShouldBe("/var/lib/postgresql/data/engine-upgrade");
    }

    [Test]
    public void WhisperProviderRow_IsFrozen()
    {
        WhisperPluginConstants.ProviderId.ShouldBe(Guid.Parse("7f3d2a10-0000-4000-8000-57705731a001"));
        WhisperPluginConstants.ProviderName.ShouldBe("Whisper (self-hosted)");
        WhisperPluginConstants.ParakeetProviderName.ShouldBe("Parakeet (self-hosted)");
        WhisperPluginConstants.ConnectionTypeRest.ShouldBe("rest");
        WhisperPluginConstants.ModelsPath.ShouldBe("/v1/models");
        WhisperPluginConstants.ModelAliasSmall.ShouldBe("small");
        WhisperPluginConstants.ModelAliasLarge.ShouldBe("large-v3-turbo");
        WhisperPluginConstants.ModelAliasParakeet.ShouldBe("parakeet-v3");
    }

    [Test]
    public void WhisperModelIds_AreFrozen()
    {
        WhisperPluginConstants.SmallModelId.ShouldBe("Systran/faster-whisper-small");
        WhisperPluginConstants.LargeModelId.ShouldBe("deepdml/faster-whisper-large-v3-turbo-ct2");
        WhisperPluginConstants.ParakeetModelId.ShouldBe("parakeet-tdt-0.6b-v3-int8");
    }

    [Test]
    public void WhisperOptionDefaults_AreFrozen()
    {
        var defaults = new UpdaterOptions();

        defaults.WhisperServiceName.ShouldBe("whisper-stt");
        defaults.WhisperComposeProfile.ShouldBe("whisper");
        defaults.WhisperBaseUrl.ShouldBe("http://whisper-stt:8000");
    }

    [Test]
    public void SttEngineOptionNames_AreFrozen()
    {
        var names = typeof(UpdaterOptions).GetProperties().Select(p => p.Name).ToList();

        names.ShouldContain("SttServiceName");
        names.ShouldContain("SttComposeProfile");
        names.ShouldContain("SttBaseUrl");
        names.ShouldContain("NativeSttServiceName");
    }

    [Test]
    public void ArtifactSignaturePayload_IsVersionRefAndShaSeparatedByNewlines()
    {
        var operation = new UpdateOperation
        {
            Id = Guid.Empty,
            OperationType = UpdateOperationType.Update,
            TargetVersion = "1.2.3",
            ArtifactRef = "ghcr.io/heribertg/klacks-api:1.2.3",
            ArtifactSha256 = "abc123",
        };

        ArtifactSignaturePayload.Build(operation).ShouldBe("1.2.3\nghcr.io/heribertg/klacks-api:1.2.3\nabc123");
    }
}
