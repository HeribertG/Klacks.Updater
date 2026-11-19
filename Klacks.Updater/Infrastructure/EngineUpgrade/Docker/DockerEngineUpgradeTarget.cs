// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Docker on-prem engine upgrade target. Placeholder until the Docker adapter lands: it reports the deployment as
/// unsupported, so the executor fails every engine upgrade before anything is changed.
/// </summary>
namespace Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

public class DockerEngineUpgradeTarget : UnsupportedEngineUpgradeTarget
{
}
