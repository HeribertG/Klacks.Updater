// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Tests;

using Klacks.Updater.Application;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class SttVersionLabelTests
{
    [Test]
    public void Key_IsFrozen()
    {
        SttVersionLabel.Key.ShouldBe("org.klacks.stt-version");
    }

    [Test]
    public void UpdaterOptions_SttTagEnvVar_DefaultsToKlacksSttTag()
    {
        new UpdaterOptions().SttTagEnvVar.ShouldBe("KLACKS_STT_TAG");
    }

    [TestCase("1.0.0", "1.0.0")]
    [TestCase("12.345.6789", "12.345.6789")]
    [TestCase("1.0.0\n", "1.0.0")]
    [TestCase("  1.2.3\r\n", "1.2.3")]
    public void Parse_ValidVersion_ReturnsTrimmedValue(string raw, string expected)
    {
        SttVersionLabel.Parse(raw).ShouldBe(expected);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("<no value>")]
    [TestCase("1.0")]
    [TestCase("1")]
    [TestCase("v1.0.0")]
    [TestCase("1.0.0-rc1")]
    [TestCase("1.0.0+build")]
    [TestCase("1.0.0.0")]
    [TestCase("1.0.x")]
    [TestCase("1.0.0\n2.0.0")]
    [TestCase("1.0.0 ; rm -rf /")]
    [TestCase("latest")]
    [TestCase("١.٠.٠")]
    public void Parse_MissingOrInvalid_ReturnsNull(string? raw)
    {
        SttVersionLabel.Parse(raw).ShouldBeNull();
    }
}
