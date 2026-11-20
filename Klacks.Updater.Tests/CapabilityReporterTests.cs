// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class CapabilityReporterTests
{
    private EngineUpgradeHarness _engine = null!;
    private IUpdateOperationStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _engine = new EngineUpgradeHarness();
        _store = Substitute.For<IUpdateOperationStore>();
    }

    [TearDown]
    public void TearDown() => _engine.Dispose();

    private CapabilityReporter Reporter() =>
        new(_store, _engine.Target, _engine.Journal, Microsoft.Extensions.Options.Options.Create(_engine.Options), NullLogger<CapabilityReporter>.Instance);

    [Test]
    public async Task Supported_target_reports_the_engine_upgrade_capability()
    {
        (await Reporter().ReportOnceAsync(CancellationToken.None)).ShouldBeTrue();

        await _store.Received(1).UpsertSettingAsync(
            EngineUpgradeConstants.CapabilitySettingType,
            Arg.Is<string>(value => value.Contains(EngineUpgradeConstants.EngineUpgradeCapability)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Unsupported_target_reports_no_capability()
    {
        _engine.Target.Inspection = _engine.Target.Inspection with { UnsupportedReason = "not here" };

        await Reporter().ReportOnceAsync(CancellationToken.None);

        await _store.Received(1).UpsertSettingAsync(
            EngineUpgradeConstants.CapabilitySettingType,
            Arg.Is<string>(value => value.Contains("\"capabilities\":[]")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Open_journal_pauses_the_report()
    {
        await _engine.Journal.SaveAsync(new EngineUpgradeJournalEntry { OperationId = Guid.NewGuid() }, CancellationToken.None);

        await Reporter().ReportOnceAsync(CancellationToken.None);

        _store.ReceivedCalls().ShouldBeEmpty();
        _engine.Target.Calls.ShouldBeEmpty();
    }

    [Test]
    public async Task Upgrade_starting_during_the_inspection_suppresses_the_write()
    {
        _engine.Target.DuringInspect = () =>
            _engine.Journal.SaveAsync(new EngineUpgradeJournalEntry { OperationId = Guid.NewGuid() }, CancellationToken.None);

        await Reporter().ReportOnceAsync(CancellationToken.None);

        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Test]
    public async Task Database_failure_is_swallowed()
    {
        _store.UpsertSettingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("database down"));

        (await Reporter().ReportOnceAsync(CancellationToken.None)).ShouldBeFalse();
    }
}
