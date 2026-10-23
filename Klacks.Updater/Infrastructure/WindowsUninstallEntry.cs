// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Location of the "Apps &amp; Features" entry the office-PC setup (Inno Setup, Klacks.Api
/// deploy/native/windows/installer/Klacks.iss) writes: HKLM, 64-bit view, subkey "&lt;AppId&gt;_is1". The AppId must
/// stay identical to the one in Klacks.iss (frozen by UninstallEntryContractFreezeTests).
/// </summary>

namespace Klacks.Updater.Infrastructure;

public static class WindowsUninstallEntry
{
    public const string AppId = "{6F1B7D52-3C0E-4B8A-9E41-5A2D8C7F0B13}";

    public const string InnoSetupKeySuffix = "_is1";

    public const string UninstallRootPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public const string SubKeyPath = UninstallRootPath + @"\" + AppId + InnoSetupKeySuffix;

    public const string DisplayVersionValueName = "DisplayVersion";
}
