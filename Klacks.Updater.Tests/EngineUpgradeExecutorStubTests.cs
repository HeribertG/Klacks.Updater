// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;
using Klacks.Updater.Domain.EngineUpgrade;
using Klacks.Updater.Infrastructure.EngineUpgrade;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class EngineUpgradeExecutorStubTests
{
    private static UpdateOperation Operation() => new()
    {
        Id = Guid.NewGuid(),
        OperationType = UpdateOperationType.PostgresUpgrade,
        FromVersion = "17",
        TargetVersion = "19",
        ArtifactRef = "ghcr.io/heribertg/klacks-postgres:19",
        ArtifactSha256 = "sha256:abc",
    };

    [Test]
    public async Task Unsupported_target_fails_the_operation_with_its_reason()
    {
        var executor = new EngineUpgradeExecutor(new UnsupportedEngineUpgradeTarget(), NullLogger<EngineUpgradeExecutor>.Instance);

        var result = await executor.ExecuteAsync(Operation());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain(UnsupportedEngineUpgradeTarget.Reason);
    }

    [Test]
    public async Task Supported_target_is_still_not_touched_in_this_build()
    {
        var target = Substitute.For<IEngineUpgradeTarget>();
        target.InspectAsync(Arg.Any<CancellationToken>()).Returns(new EngineInspection(17, new Dictionary<string, string>(), long.MaxValue, null));
        var executor = new EngineUpgradeExecutor(target, NullLogger<EngineUpgradeExecutor>.Instance);

        var result = await executor.ExecuteAsync(Operation());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain(EngineUpgradeExecutor.NotAvailableMessage);
        target.ReceivedCalls().Count().ShouldBe(1);
    }

    [Test]
    public async Task Failing_inspection_is_failed_not_rollback_failed()
    {
        var target = Substitute.For<IEngineUpgradeTarget>();
        target.InspectAsync(Arg.Any<CancellationToken>()).Returns<EngineInspection>(_ => throw new InvalidOperationException("docker down"));
        var executor = new EngineUpgradeExecutor(target, NullLogger<EngineUpgradeExecutor>.Instance);

        var result = await executor.ExecuteAsync(Operation());

        result.Status.ShouldBe(UpdateExecutionStatus.Failed);
        result.Message.ShouldContain(EngineUpgradeExecutor.InspectionFailedMessage);
    }
}
