// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class EngineUpgradeRecoveryTests
{
    private static IEnumerable<EngineUpgradePhase> OpenPhases() =>
        Enum.GetValues<EngineUpgradePhase>().Where(phase => phase < EngineUpgradePhase.ConfigurationCommitted);

    private static EngineUpgradeJournalEntry Entry(EngineUpgradePhase phase) => new()
    {
        Schema = 1,
        OperationId = Guid.NewGuid(),
        Mode = "Docker",
        FromMajor = 17,
        ToMajor = 19,
        Phase = phase,
        Old = new Dictionary<string, string> { ["oldImage"] = "pg17" },
        AppVersion = EngineUpgradeHarness.AppVersion,
        Message = "earlier message",
    };

    [Test]
    public async Task No_journal_means_nothing_to_recover()
    {
        using var harness = new EngineUpgradeHarness();

        (await harness.Recovery.RecoverAsync(CancellationToken.None)).ShouldBeNull();

        harness.Target.Calls.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(OpenPhases))]
    public async Task Open_journal_is_reverted_from_its_content_alone(EngineUpgradePhase phase)
    {
        using var harness = new EngineUpgradeHarness();
        var entry = Entry(phase);
        await harness.Journal.SaveAsync(entry, CancellationToken.None);

        var result = await harness.Recovery.RecoverAsync(CancellationToken.None);

        result!.Status.ShouldBe(UpdateExecutionStatus.RolledBack);
        harness.Target.Calls.ShouldBe([FakeEngineUpgradeTarget.StopApplication, FakeEngineUpgradeTarget.Revert, FakeEngineUpgradeTarget.StartApplication]);
        harness.Target.RevertContext!.OldState["oldImage"].ShouldBe("pg17");
        harness.Target.RevertContext.FromMajor.ShouldBe(17);
        await harness.Store.Received(1).CompleteAsync(entry.OperationId, UpdateExecutionStatus.RolledBack, Arg.Any<string>(), Arg.Is<string?>(value => value == null), Arg.Any<CancellationToken>());
        harness.Journal.Exists().ShouldBeFalse();
    }

    [TestCase(EngineUpgradePhase.ConfigurationCommitted)]
    [TestCase(EngineUpgradePhase.ApplicationHealthy)]
    public async Task Interrupted_after_configuration_commit_rolls_forward_when_the_application_is_healthy(EngineUpgradePhase phase)
    {
        using var harness = new EngineUpgradeHarness();
        var entry = Entry(phase);
        await harness.Journal.SaveAsync(entry, CancellationToken.None);

        var result = await harness.Recovery.RecoverAsync(CancellationToken.None);

        result!.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        harness.Target.Calls.ShouldBe([FakeEngineUpgradeTarget.StartApplication]);
        harness.Journal.Exists().ShouldBeFalse();
    }

    [Test]
    public async Task Interrupted_after_configuration_commit_reverts_when_the_application_stays_unhealthy()
    {
        using var harness = new EngineUpgradeHarness();
        harness.HealthGate.WaitForHealthyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false, true);
        await harness.Journal.SaveAsync(Entry(EngineUpgradePhase.ConfigurationCommitted), CancellationToken.None);

        var result = await harness.Recovery.RecoverAsync(CancellationToken.None);

        result!.Status.ShouldBe(UpdateExecutionStatus.RolledBack);
        harness.Target.Calls.ShouldContain(FakeEngineUpgradeTarget.Revert);
    }

    [Test]
    public async Task Committed_journal_only_marks_the_row_succeeded()
    {
        using var harness = new EngineUpgradeHarness();
        var entry = Entry(EngineUpgradePhase.Committed);
        await harness.Journal.SaveAsync(entry, CancellationToken.None);

        var result = await harness.Recovery.RecoverAsync(CancellationToken.None);

        result!.Status.ShouldBe(UpdateExecutionStatus.Succeeded);
        harness.Target.Calls.ShouldBeEmpty();
        await harness.Store.Received(1).CompleteAsync(entry.OperationId, UpdateExecutionStatus.Succeeded, Arg.Is("earlier message"), Arg.Is<string?>(value => value == null), Arg.Any<CancellationToken>());
        harness.Journal.Exists().ShouldBeFalse();
    }

    [TestCase(EngineUpgradePhase.RolledBack, UpdateExecutionStatus.RolledBack)]
    [TestCase(EngineUpgradePhase.RollbackFailed, UpdateExecutionStatus.RollbackFailed)]
    public async Task Terminal_journal_only_rewrites_the_row(EngineUpgradePhase phase, UpdateExecutionStatus expected)
    {
        using var harness = new EngineUpgradeHarness();
        await harness.Journal.SaveAsync(Entry(phase), CancellationToken.None);

        var result = await harness.Recovery.RecoverAsync(CancellationToken.None);

        result!.Status.ShouldBe(expected);
        harness.Target.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Failing_revert_during_recovery_is_rollback_failed()
    {
        using var harness = new EngineUpgradeHarness();
        harness.Target.FailRevert = true;
        await harness.Journal.SaveAsync(Entry(EngineUpgradePhase.NewEngineActive), CancellationToken.None);

        var result = await harness.Recovery.RecoverAsync(CancellationToken.None);

        result!.Status.ShouldBe(UpdateExecutionStatus.RollbackFailed);
    }

    [Test]
    public async Task Row_is_written_again_once_the_database_answers()
    {
        using var harness = new EngineUpgradeHarness();
        var entry = Entry(EngineUpgradePhase.Committed);
        await harness.Journal.SaveAsync(entry, CancellationToken.None);
        var attempts = 0;
        harness.Store.CompleteAsync(Arg.Any<Guid>(), Arg.Any<UpdateExecutionStatus>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++attempts == 1 ? throw new InvalidOperationException("database down") : Task.CompletedTask);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        await harness.Recovery.RecoverAsync(timeout.Token);

        attempts.ShouldBe(2);
        harness.Journal.Exists().ShouldBeFalse();
    }

    [Test]
    public async Task Unreadable_journal_is_left_in_place()
    {
        using var harness = new EngineUpgradeHarness();
        Directory.CreateDirectory(harness.EngineUpgradeDirectory);
        await File.WriteAllTextAsync(harness.Journal.JournalPath, "{ not json");

        (await harness.Recovery.RecoverAsync(CancellationToken.None)).ShouldBeNull();

        harness.Journal.Exists().ShouldBeTrue();
        harness.Target.Calls.ShouldBeEmpty();
    }
}
