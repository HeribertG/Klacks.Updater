// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

/// <summary>
/// Keys of the journaled OldState/NewState maps of the Docker engine upgrade. The inspection records the raw .env values
/// of both compose variables (or Missing when the key is absent) and the effective image and data subdirectory before
/// anything changes; the later steps record the pinned image, the new subdirectory, the dumped databases and the dump folder.
/// </summary>
public static class DockerEngineStateKeys
{
    public const string Missing = "missing";

    public const string EnvImage = "docker.env.image";

    public const string EnvDataSubdir = "docker.env.dataSubdir";

    public const string Image = "docker.image";

    public const string DataSubdir = "docker.dataSubdir";

    public const string Databases = "docker.databases";

    public const string DumpDirectory = "docker.dumpDirectory";
}
