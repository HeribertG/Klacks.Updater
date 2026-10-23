// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

// Mirrors AppId in Klacks.Api/deploy/native/windows/installer/Klacks.iss ("{{" there is Inno's escape for "{") and
// Inno Setup's uninstall key convention "<AppId>_is1" — change both deliberately.
namespace Klacks.Updater.Tests;

using Klacks.Updater.Infrastructure;
using NUnit.Framework;
using Shouldly;

[TestFixture]
public class UninstallEntryContractFreezeTests
{
    [Test]
    public void SetupAppId_IsFrozen()
    {
        WindowsUninstallEntry.AppId.ShouldBe("{6F1B7D52-3C0E-4B8A-9E41-5A2D8C7F0B13}");
    }

    [Test]
    public void UninstallEntryKey_IsFrozen()
    {
        WindowsUninstallEntry.SubKeyPath.ShouldBe(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{6F1B7D52-3C0E-4B8A-9E41-5A2D8C7F0B13}_is1");
        WindowsUninstallEntry.DisplayVersionValueName.ShouldBe("DisplayVersion");
    }
}
