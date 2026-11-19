// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Wires the engine upgrade executor and recovery with fakes, a real file journal in a temporary directory and the
/// real RSA verifier (unsigned engine upgrades allowed unless a key is configured).
/// </summary>
namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using Klacks.Updater.Infrastructure;
using Klacks.Updater.Infrastructure.EngineUpgrade;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

public sealed class EngineUpgradeHarness : IDisposable
{
    public const string AppVersion = "1.0.79";
    public const string SecretPassword = "S3cr3t-Passw0rd";

    public EngineUpgradeHarness(EngineUpgradePhase? faultPhase = null, string signaturePublicKey = "")
    {
        BackupDirectory = Path.Combine(Path.GetTempPath(), "klacks-engine-upgrade-" + Guid.NewGuid().ToString("N"));
        Options = new UpdaterOptions
        {
            BackupDirectory = BackupDirectory,
            ConnectionString = $"Host=postgres;Database=Klacks;Username=admin;Password={SecretPassword}",
            AllowUnsignedEngineUpgrade = string.IsNullOrEmpty(signaturePublicKey),
            SignaturePublicKey = signaturePublicKey,
            EngineUpgradeFaultPhase = faultPhase,
        };
        Reader.IsRestored = () => Target.Calls.Contains(FakeEngineUpgradeTarget.Restore);
        VersionReader.ReadRunningVersionAsync(Arg.Any<CancellationToken>()).Returns(AppVersion);
        HealthGate.WaitForHealthyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var options = Microsoft.Extensions.Options.Options.Create(Options);
        Journal = new FileEngineUpgradeJournal(options);
        Finisher = new EngineUpgradeFinisher(Target, Reader, HealthGate, Journal, Store, NullLogger<EngineUpgradeFinisher>.Instance, TimeSpan.FromMilliseconds(10));
        Executor = new EngineUpgradeExecutor(
            Target,
            Reader,
            new RsaArtifactVerifier(options, NullLogger<RsaArtifactVerifier>.Instance),
            VersionReader,
            HealthGate,
            Journal,
            Finisher,
            options,
            NullLogger<EngineUpgradeExecutor>.Instance);
        Recovery = new EngineUpgradeRecovery(Journal, Finisher, NullLogger<EngineUpgradeRecovery>.Instance);
    }

    public string BackupDirectory { get; }

    public UpdaterOptions Options { get; }

    public FakeEngineUpgradeTarget Target { get; } = new();

    public FakeEngineSnapshotReader Reader { get; } = new();

    public IApplicationVersionReader VersionReader { get; } = Substitute.For<IApplicationVersionReader>();

    public IUpdateHealthGate HealthGate { get; } = Substitute.For<IUpdateHealthGate>();

    public IUpdateOperationStore Store { get; } = Substitute.For<IUpdateOperationStore>();

    public FileEngineUpgradeJournal Journal { get; }

    public EngineUpgradeFinisher Finisher { get; }

    public EngineUpgradeExecutor Executor { get; }

    public EngineUpgradeRecovery Recovery { get; }

    public string EngineUpgradeDirectory => Path.Combine(BackupDirectory, UpdaterConstants.EngineUpgradeDirectoryName);

    public static UpdateOperation Operation(string from = "17", string to = "19") => new()
    {
        Id = Guid.NewGuid(),
        OperationType = UpdateOperationType.PostgresUpgrade,
        FromVersion = from,
        TargetVersion = to,
        ArtifactRef = "ghcr.io/heribertg/klacks-postgres:19",
        ArtifactSha256 = "sha256:" + new string('a', 64),
    };

    public string ArchivedJournal(Guid operationId) =>
        File.ReadAllText(Path.Combine(EngineUpgradeDirectory, string.Format(EngineUpgradeConstants.ArchivedJournalFilePattern, operationId)));

    public void Dispose()
    {
        if (Directory.Exists(BackupDirectory))
        {
            Directory.Delete(BackupDirectory, recursive: true);
        }
    }
}
