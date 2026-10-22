// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Infrastructure;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class ResourceCapacityEvaluatorTests
{
    private const long OneGigabyte = 1024L * 1024 * 1024;
    private const long OneMegabyte = 1024L * 1024;

    [Test]
    public void Evaluate_returns_null_when_capacity_suffices()
    {
        ResourceCapacityEvaluator.Evaluate(2 * OneGigabyte, 5 * OneGigabyte, OneGigabyte, OneGigabyte).ShouldBeNull();
    }

    [Test]
    public void Evaluate_returns_null_when_nothing_could_be_measured()
    {
        ResourceCapacityEvaluator.Evaluate(null, null, OneGigabyte, OneGigabyte).ShouldBeNull();
    }

    [Test]
    public void Evaluate_rejects_low_memory()
    {
        ResourceCapacityEvaluator.Evaluate(512 * OneMegabyte, 5 * OneGigabyte, OneGigabyte, OneGigabyte)
            .ShouldBe("Not enough free memory: 512 MB available, 1024 MB required.");
    }

    [Test]
    public void Evaluate_rejects_low_disk()
    {
        ResourceCapacityEvaluator.Evaluate(2 * OneGigabyte, 100 * OneMegabyte, OneGigabyte, OneGigabyte)
            .ShouldBe("Not enough free disk space: 100 MB available, 1024 MB required.");
    }

    [Test]
    public void Evaluate_checks_memory_before_disk()
    {
        ResourceCapacityEvaluator.Evaluate(0, 0, OneGigabyte, OneGigabyte)!.ShouldStartWith("Not enough free memory");
    }
}
