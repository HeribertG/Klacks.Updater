// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Native;

/// <summary>
/// Keys of the journaled OldState/NewState maps of the native engine upgrade. Old values are captured by the inspection
/// before anything changes, so a revert needs nothing else; none of them is a secret.
/// </summary>
public static class NativeEngineStateKeys
{
    public const string ImagePath = "imagePath";
    public const string BinDirectory = "binDirectory";
    public const string DataDirectory = "dataDirectory";
    public const string Pointer = "pointer";
    public const string ConfiguredBinDirectory = "configuredBinDirectory";
    public const string Databases = "databases";
    public const string Missing = "missing";
}
