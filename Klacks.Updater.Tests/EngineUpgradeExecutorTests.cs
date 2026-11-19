// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using System.Security.Cryptography;
using System.Text;
using Klacks.Updater.Application;
using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class EngineUpgradeExecutorTests
{
    private static readonly string[] SuccessSequence =
    [
        FakeEngineUpgradeTarget.Inspect,
        FakeEngineUpgradeTarget.Prepare,
        FakeEngineUpgradeTarget.StopApplication,
        FakeEngineUpgradeTarget.Dump,
        FakeEngineUpgradeTarget.StopOld,
        FakeEngineUpgradeTarget.Activate,
        FakeEngineUpgradeTarget.Restore,
        FakeEngineUpgradeTarget.Commit,
        FakeEngineUpgradeTarget.StartApplication,
    ];

    private static IEnumerable<EngineUpgradePhase> FaultPhases() =>
        Enum.GetValues<EngineUpgradePhase>().Where(phase => phase < EngineUpgradePhase.Committed);

    private static IEnumerable<string> FailingSteps() =>
    [
        FakeEngineUpgradeTarget.Prepare,
        FakeEngineUpgradeTarget.StopApplication,
        FakeEngineUpgradeTarget.Dump,
        FakeEngineUpgradeTarget.StopOld,
        FakeEngineUpgradeTarget.Activate,
        FakeEngineUpgradeTarget.Restore,
        FakeEngineUpgradeTarget.Commit,
    ];

    [Test]
    public async Task Success_runs_every_step_in_order_and_commits()
    {
        using var harness = new EngineUpgradeHarness();
        var operation = EngineUpgradeHarness.Operation();

        var result = await harness.Executor.ExecuteAsync(operation);

        result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        harness.Target.Calls.ShouldBe(SuccessSequence);
        harness.Reader.Analyzed.ShouldBe(["Klacks", "klacks_diag"]);
        harness.Reader.PoolClears.ShouldBe(1);
        await harness.HealthGate.Received(1).WaitForHealthyAsync(EngineUpgradeHarness.AppVersion, Arg.Any<CancellationToken>());
        await harness.Store.Received(1).CompleteAsync(operation.Id, UpdateExecutionStatus.Succeeded, Arg.Any<string>(), Arg.Is<string?>(value => value == null), Arg.Any<CancellationToken>());
        harness.Journal.Exists().ShouldBeFalse();
        var archived = harness.ArchivedJournal(operation.Id);
        archived.ShouldContain("\"phase\": \"Committed\"");
        archived.ShouldContain("\"oldImage\": \"pg17\"");
    }

    [TestCaseSource(nameof(FaultPhases))]
    public async Task Fault_after_any_phase_before_commit_reverts_to_the_old_engine(EngineUpgradePhase phase)
    {
        using var harness = new EngineUpgradeHarness(faultPhase: phase);
        var operation = EngineUpgradeHarness.Operation();

        var result = await harness.Executor.ExecuteAsync(operation);

        result.Status.ShouldBe(UpdateExecutionStatus.RolledBack);
        harness.Target.Calls.ShouldContain(FakeEngineUpgradeTarget.Revert);
        harness.Target.Calls[^1].ShouldBe(FakeEngineUpgradeTarget.StartApplication);
        await harness.Store.Received(1).CompleteAsync(operation.Id, UpdateExecutionStatus.RolledBack, Arg.Any<string>(), Arg.Is<string?>(value => value == null), Arg.Any<CancellationToken>());
        harness.ArchivedJournal(operation.Id).ShouldContain("\"phase\": \"RolledBack\"");
        harness.Journal.Exists().ShouldBeFalse();
    }

    [TestCaseSource(nameof(FailingSteps))]
    public async Task Failing_step_reverts_and_reports_rolled_back(string step)
    {
        using var harness = new EngineUpgradeHarness();
        harness.Target.FailAt = step;

        var result = await harness.Executor.ExecuteAsync(EngineUpgradeHarness.Operation());

        result.Status.ShouldBe(UpdateExecutionStatus.RolledBack);
        result.Message.ShouldContain($"{step} failed");
        harness.Target.Calls.ShouldContain(FakeEngineUpgradeTarget.Revert);
    }

    [Test]
    public async Task Revert_failure_is_rollback_failed()
    {
        using var harness = new EngineUpgradeHarness(faultPhase: EngineUpgradePhase.Restored);
        harness.Target.FailRevert = true;
        var operation = EngineUpgradeHarness.Operation();

        var result = await harness.Executor.ExecuteAsync(operation);

        result.Status.ShouldBe(UpdateExecutionStatus.RollbackFailed);
        harness.ArchivedJournal(operation.Id).ShouldContain("\"phase\": \"RollbackFailed\"");
    }

    [Test]
    public async Task Unhealthy_application_after_upgrade_is_reverted_and_unhealthy_after_revert_is_rollback_failed()
    {
        using var harness = new EngineUpgradeHarness();
        harness.HealthGate.WaitForHealthyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await harness.Executor.ExecuteAsync(EngineUpgradeHarness.Operation());

        result.Status.ShouldBe(UpdateExecutionStatus.RollbackFailed);
        harness.Target.Calls.ShouldContain(FakeEngineUpgradeTarget.Revert);
    }

    [Test]
    public async Task Restore_with_missing_rows_fails_verification_and_reverts()
    {
        using var harness = new EngineUpgradeHarness();
        harness.Reader.AfterRestore = database => FakeEngineSnapshotReader.Snapshot(database) with
        {
            RowCounts = new Dictionary<string, long> { ["public.clients"] = 41 },
        };

        var result = await harness.Executor.ExecuteAsync(EngineUpgradeHarness.Operation());

        result.Status.ShouldBe(UpdateExecutionStatus.RolledBack);
        result.Message.ShouldContain("41 rows instead of 42");
        harness.Target.Calls.ShouldNotContain(FakeEngineUpgradeTarget.Commit);
    }

    [Test]
    public async Task Journal_never_contains_the_database_password()
    {
        using var harness = new EngineUpgradeHarness(faultPhase: EngineUpgradePhase.Verified);
        var operation = EngineUpgradeHarness.Operation();

        await harness.Executor.ExecuteAsync(operation);

        harness.ArchivedJournal(operation.Id).ShouldNotContain(EngineUpgradeHarness.SecretPassword);
    }

    [Test]
    public async Task Source_major_different_from_the_server_fails_without_any_change()
    {
        using var harness = new EngineUpgradeHarness();

        var result = await harness.Executor.ExecuteAsync(EngineUpgradeHarness.Operation(from: "16"));

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldEndWith(UpdaterConstants.NothingChangedSuffix);
        harness.Target.Calls.ShouldBe([FakeEngineUpgradeTarget.Inspect]);
        harness.Journal.Exists().ShouldBeFalse();
    }

    [Test]
    public async Task Foreign_role_fails_without_any_change()
    {
        using var harness = new EngineUpgradeHarness();
        harness.Reader.Facts = harness.Reader.Facts with { ForeignRoles = ["reporting"] };

        var result = await harness.Executor.ExecuteAsync(EngineUpgradeHarness.Operation());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain("reporting");
        harness.Target.Calls.ShouldBe([FakeEngineUpgradeTarget.Inspect]);
    }

    [Test]
    public async Task Open_journal_blocks_a_new_engine_upgrade()
    {
        using var harness = new EngineUpgradeHarness();
        await harness.Journal.SaveAsync(new EngineUpgradeJournalEntry { OperationId = Guid.NewGuid() }, CancellationToken.None);

        var result = await harness.Executor.ExecuteAsync(EngineUpgradeHarness.Operation());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain(EngineUpgradeExecutor.JournalOpenMessage);
        harness.Target.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Unsigned_mode_rejects_an_engine_upgrade()
    {
        using var harness = new EngineUpgradeHarness();
        harness.Options.AllowUnsignedEngineUpgrade = false;

        var result = await harness.Executor.ExecuteAsync(EngineUpgradeHarness.Operation());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain(EngineUpgradeExecutor.SignatureRejectedMessage);
        harness.Target.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Valid_engine_signature_is_accepted_and_an_application_signature_is_not()
    {
        using var rsa = RSA.Create(2048);
        var operation = EngineUpgradeHarness.Operation();
        var applicationPayload = $"{operation.TargetVersion}\n{operation.ArtifactRef}\n{operation.ArtifactSha256}";
        var enginePayload = ArtifactSignaturePayload.Build(operation);

        using (var harness = new EngineUpgradeHarness(signaturePublicKey: rsa.ExportSubjectPublicKeyInfoPem()))
        {
            var result = await harness.Executor.ExecuteAsync(operation with { ArtifactSignature = Sign(rsa, applicationPayload) });
            result.Status.ShouldBe(UpdateExecutionStatus.Failed);
            harness.Target.Calls.ShouldBeEmpty();
        }

        using (var harness = new EngineUpgradeHarness(signaturePublicKey: rsa.ExportSubjectPublicKeyInfoPem()))
        {
            var result = await harness.Executor.ExecuteAsync(operation with { ArtifactSignature = Sign(rsa, enginePayload) });
            result.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        }
    }

    [Test]
    public void Cancellation_leaves_the_journal_open_for_recovery()
    {
        using var harness = new EngineUpgradeHarness();
        using var cancellation = new CancellationTokenSource();
        harness.HealthGate.WaitForHealthyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<bool>(_ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });

        Should.Throw<OperationCanceledException>(() => harness.Executor.ExecuteAsync(EngineUpgradeHarness.Operation(), cancellation.Token));

        harness.Journal.Exists().ShouldBeTrue();
        harness.Target.Calls.ShouldNotContain(FakeEngineUpgradeTarget.Revert);
    }

    private static string Sign(RSA rsa, string payload) =>
        Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
}
