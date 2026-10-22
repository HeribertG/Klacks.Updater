// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Infrastructure;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class ComposeServicesTests
{
    private const string Output = "postgres\r\nklacks-api\nklacks-stt\n\n";

    [Test]
    public void Contains_finds_a_listed_service_regardless_of_line_endings()
    {
        ComposeServices.Contains(Output, "klacks-stt").ShouldBeTrue();
        ComposeServices.Contains(Output, "postgres").ShouldBeTrue();
    }

    [TestCase("whisper-stt")]
    [TestCase("klacks")]
    [TestCase("KLACKS-STT")]
    [TestCase("")]
    public void Contains_rejects_services_that_are_not_listed(string serviceName)
    {
        ComposeServices.Contains(Output, serviceName).ShouldBeFalse();
    }

    [Test]
    public void Contains_is_false_for_empty_output()
    {
        ComposeServices.Contains(string.Empty, "klacks-stt").ShouldBeFalse();
    }

    [Test]
    public void MissingServiceMessage_tells_the_operator_to_fetch_the_current_bundle()
    {
        ComposeServices.MissingServiceMessage("whisper-stt").ShouldBe(
            "The compose file does not define the service 'whisper-stt'. Download the current on-prem bundle and retry.");
    }
}
