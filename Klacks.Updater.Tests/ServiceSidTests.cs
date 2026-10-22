// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Infrastructure;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class ServiceSidTests
{
    [Test]
    public void FromName_matches_the_sid_windows_assigns_to_the_spooler_service()
    {
        ServiceSid.FromName("Spooler").ShouldBe("S-1-5-80-3951239711-1671533544-1416304335-3763227691-3930497994");
    }

    [Test]
    public void FromName_ignores_the_case_of_the_name()
    {
        ServiceSid.FromName("spooler").ShouldBe(ServiceSid.FromName("SPOOLER"));
    }

    [Test]
    public void FromName_differs_per_service()
    {
        ServiceSid.FromName("KlacksStt").ShouldNotBe(ServiceSid.FromName("Klacks"));
    }
}
