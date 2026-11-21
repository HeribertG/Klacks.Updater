// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class EngineUpgradeRequestExpiryTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 10, 0, 0, DateTimeKind.Utc);

    private static UpdateOperation Operation(UpdateOperationType type, TimeSpan age) => new()
    {
        Id = Guid.NewGuid(),
        OperationType = type,
        RequestedAt = Now - age,
    };

    [Test]
    public void ExactlyThirtyMinutesOld_IsNotExpired()
    {
        EngineUpgradeRequestExpiry.IsExpired(Operation(UpdateOperationType.PostgresUpgrade, TimeSpan.FromMinutes(30)), Now)
            .ShouldBeFalse();
    }

    [Test]
    public void OneSecondOverThirtyMinutes_IsExpired()
    {
        EngineUpgradeRequestExpiry.IsExpired(
                Operation(UpdateOperationType.PostgresUpgrade, TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1)), Now)
            .ShouldBeTrue();
    }

    [TestCase(UpdateOperationType.Update)]
    [TestCase(UpdateOperationType.Rollback)]
    [TestCase(UpdateOperationType.WhisperInstall)]
    [TestCase(UpdateOperationType.WhisperUninstall)]
    public void OtherOperationTypes_NeverExpire(UpdateOperationType type)
    {
        EngineUpgradeRequestExpiry.IsExpired(Operation(type, TimeSpan.FromDays(3)), Now).ShouldBeFalse();
    }

    [Test]
    public void MaxAge_IsThirtyMinutes()
    {
        EngineUpgradeConstants.PendingEngineUpgradeMaxAge.ShouldBe(TimeSpan.FromMinutes(30));
    }
}
