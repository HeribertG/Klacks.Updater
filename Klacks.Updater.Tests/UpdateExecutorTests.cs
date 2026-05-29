// Copyright (c) Heribert Gasparoli Private. All rights reserved.

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class UpdateExecutorTests
{
    private IUpdateApplier _applier = null!;
    private IUpdateBackupService _backup = null!;
    private IUpdateHealthGate _health = null!;
    private IArtifactVerifier _verifier = null!;
    private UpdateExecutor _executor = null!;

    [SetUp]
    public void SetUp()
    {
        _applier = Substitute.For<IUpdateApplier>();
        _backup = Substitute.For<IUpdateBackupService>();
        _health = Substitute.For<IUpdateHealthGate>();
        _verifier = Substitute.For<IArtifactVerifier>();
        _verifier.Verify(Arg.Any<UpdateOperation>()).Returns(true);
        _executor = new UpdateExecutor(_applier, _backup, _health, _verifier, NullLogger<UpdateExecutor>.Instance);
    }

    private static UpdateOperation UpdateOp(bool migrations = false) => new()
    {
        Id = Guid.NewGuid(),
        OperationType = UpdateOperationType.Update,
        FromVersion = "1.0.0",
        TargetVersion = "1.1.0",
        ArtifactRef = "ghcr.io/x/api:1.1.0",
        ContainsMigrations = migrations,
    };

    [Test]
    public async Task Healthy_update_succeeds()
    {
        _health.WaitForHealthyAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await _executor.ExecuteAsync(UpdateOp());

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        await _applier.Received(1).ActivateAsync("ghcr.io/x/api:1.1.0", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Backup_only_taken_when_migrations_present()
    {
        _health.WaitForHealthyAsync(Arg.Any<CancellationToken>()).Returns(true);

        await _executor.ExecuteAsync(UpdateOp(migrations: false));
        await _backup.DidNotReceive().CreateAsync(Arg.Any<CancellationToken>());

        await _executor.ExecuteAsync(UpdateOp(migrations: true));
        await _backup.Received(1).CreateAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Unhealthy_update_rolls_back_in_correct_order()
    {
        _backup.CreateAsync(Arg.Any<CancellationToken>()).Returns("backup-1");
        _health.WaitForHealthyAsync(Arg.Any<CancellationToken>()).Returns(false, true);

        var result = await _executor.ExecuteAsync(UpdateOp(migrations: true));

        result.Status.ShouldBe(UpdateExecutionStatus.RolledBack);
        Received.InOrder(() =>
        {
            _applier.ActivateAsync("ghcr.io/x/api:1.1.0", Arg.Any<CancellationToken>());
            _applier.StopAsync(Arg.Any<CancellationToken>());
            _backup.RestoreAsync("backup-1", Arg.Any<CancellationToken>());
            _applier.ActivateVersionAsync("1.0.0", Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task Rollback_does_not_restore_when_no_backup_taken()
    {
        _health.WaitForHealthyAsync(Arg.Any<CancellationToken>()).Returns(false, true);

        var result = await _executor.ExecuteAsync(UpdateOp(migrations: false));

        result.Status.ShouldBe(UpdateExecutionStatus.RolledBack);
        await _backup.DidNotReceive().RestoreAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _applier.Received(1).ActivateVersionAsync("1.0.0", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Rollback_health_failure_yields_rollback_failed()
    {
        _health.WaitForHealthyAsync(Arg.Any<CancellationToken>()).Returns(false, false);

        var result = await _executor.ExecuteAsync(UpdateOp());

        result.Status.ShouldBe(UpdateExecutionStatus.RollbackFailed);
    }

    [Test]
    public async Task Restore_failure_yields_rollback_failed()
    {
        _backup.CreateAsync(Arg.Any<CancellationToken>()).Returns("backup-1");
        _health.WaitForHealthyAsync(Arg.Any<CancellationToken>()).Returns(false);
        _backup.RestoreAsync("backup-1", Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("psql failed")));

        var result = await _executor.ExecuteAsync(UpdateOp(migrations: true));

        result.Status.ShouldBe(UpdateExecutionStatus.RollbackFailed);
    }

    [Test]
    public async Task Apply_failure_triggers_rollback()
    {
        _applier.ActivateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new InvalidOperationException("pull failed")));
        _health.WaitForHealthyAsync(Arg.Any<CancellationToken>()).Returns(true);

        var result = await _executor.ExecuteAsync(UpdateOp());

        result.Status.ShouldBe(UpdateExecutionStatus.RolledBack);
        await _applier.Received(1).ActivateVersionAsync("1.0.0", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Failed_signature_verification_rejects_without_applying()
    {
        _verifier.Verify(Arg.Any<UpdateOperation>()).Returns(false);

        var result = await _executor.ExecuteAsync(UpdateOp(migrations: true));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        await _backup.DidNotReceive().CreateAsync(Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().ActivateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _applier.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Explicit_rollback_restores_paired_backup_and_target_version()
    {
        _health.WaitForHealthyAsync(Arg.Any<CancellationToken>()).Returns(true);
        var op = new UpdateOperation
        {
            Id = Guid.NewGuid(),
            OperationType = UpdateOperationType.Rollback,
            FromVersion = "1.1.0",
            TargetVersion = "1.0.0",
            BackupRef = "backup-paired",
            ContainsMigrations = true,
        };

        var result = await _executor.ExecuteAsync(op);

        result.Status.ShouldBe(UpdateExecutionStatus.RolledBack);
        Received.InOrder(() =>
        {
            _applier.StopAsync(Arg.Any<CancellationToken>());
            _backup.RestoreAsync("backup-paired", Arg.Any<CancellationToken>());
            _applier.ActivateVersionAsync("1.0.0", Arg.Any<CancellationToken>());
        });
    }
}
