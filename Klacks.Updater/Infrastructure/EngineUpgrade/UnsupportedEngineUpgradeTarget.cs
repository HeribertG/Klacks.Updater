// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Engine upgrade target for deployments without an engine upgrade adapter: inspection reports the reason, every
/// changing step refuses, so the executor fails the operation before anything is touched.
/// </summary>
using System.Collections.ObjectModel;
using Klacks.Updater.Domain.EngineUpgrade;

namespace Klacks.Updater.Infrastructure.EngineUpgrade;

public class UnsupportedEngineUpgradeTarget : IEngineUpgradeTarget
{
    public const string Reason = "PostgreSQL engine upgrades are not supported on this deployment.";

    public Task<EngineInspection> InspectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new EngineInspection(0, ReadOnlyDictionary<string, string>.Empty, 0, Reason));

    public Task PrepareArtifactAsync(EngineUpgradeContext context, CancellationToken cancellationToken) => throw Unsupported();

    public Task StopApplicationAsync(CancellationToken cancellationToken) => throw Unsupported();

    public Task StartApplicationAsync(CancellationToken cancellationToken) => throw Unsupported();

    public Task<IReadOnlyList<EngineDump>> DumpAsync(EngineUpgradeContext context, IReadOnlyList<EngineDatabase> databases, CancellationToken cancellationToken) =>
        throw Unsupported();

    public Task StopOldEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken) => throw Unsupported();

    public Task ActivateNewEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken) => throw Unsupported();

    public Task RestoreAsync(EngineUpgradeContext context, IReadOnlyList<EngineDump> dumps, CancellationToken cancellationToken) => throw Unsupported();

    public Task CommitConfigurationAsync(EngineUpgradeContext context, CancellationToken cancellationToken) => throw Unsupported();

    public Task RevertToOldEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken) => Task.CompletedTask;

    private static NotSupportedException Unsupported() => new(Reason);
}
