// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Test double for IEngineUpgradeTarget: records every step in order, writes a marker into the state maps like a real
/// target, and throws in the step named by FailAt (or in the revert when FailRevert is set).
/// </summary>
namespace Klacks.Updater.Tests;

using Klacks.Updater.Domain.EngineUpgrade;

public sealed class FakeEngineUpgradeTarget : IEngineUpgradeTarget
{
    public const string Inspect = nameof(InspectAsync);
    public const string Prepare = nameof(PrepareArtifactAsync);
    public const string StopApplication = nameof(StopApplicationAsync);
    public const string StartApplication = nameof(StartApplicationAsync);
    public const string Dump = nameof(DumpAsync);
    public const string StopOld = nameof(StopOldEngineAsync);
    public const string Activate = nameof(ActivateNewEngineAsync);
    public const string Restore = nameof(RestoreAsync);
    public const string Commit = nameof(CommitConfigurationAsync);
    public const string Revert = nameof(RevertToOldEngineAsync);

    public List<string> Calls { get; } = [];

    public string? FailAt { get; set; }

    public bool FailRevert { get; set; }

    public EngineInspection Inspection { get; set; } = new(17, new Dictionary<string, string> { ["oldImage"] = "pg17" }, long.MaxValue, null);

    public EngineUpgradeContext? RevertContext { get; private set; }

    public Task<EngineInspection> InspectAsync(CancellationToken cancellationToken)
    {
        Calls.Add(Inspect);
        return Task.FromResult(Inspection);
    }

    public Task PrepareArtifactAsync(EngineUpgradeContext context, CancellationToken cancellationToken) => Step(Prepare, context);

    public Task StopApplicationAsync(CancellationToken cancellationToken) => Step(StopApplication, null);

    public Task StartApplicationAsync(CancellationToken cancellationToken) => Step(StartApplication, null);

    public async Task<IReadOnlyList<EngineDump>> DumpAsync(EngineUpgradeContext context, IReadOnlyList<EngineDatabase> databases, CancellationToken cancellationToken)
    {
        await Step(Dump, context);
        return databases.Select((database, index) => new EngineDump(database.Name, $"dump-{index}.pgdump")).ToList();
    }

    public Task StopOldEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken) => Step(StopOld, context);

    public Task ActivateNewEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken) => Step(Activate, context);

    public Task RestoreAsync(EngineUpgradeContext context, IReadOnlyList<EngineDump> dumps, CancellationToken cancellationToken) => Step(Restore, context);

    public Task CommitConfigurationAsync(EngineUpgradeContext context, CancellationToken cancellationToken) => Step(Commit, context);

    public Task RevertToOldEngineAsync(EngineUpgradeContext context, CancellationToken cancellationToken)
    {
        Calls.Add(Revert);
        RevertContext = context;
        if (FailRevert)
        {
            throw new InvalidOperationException("revert failed");
        }

        return Task.CompletedTask;
    }

    private Task Step(string name, EngineUpgradeContext? context)
    {
        Calls.Add(name);
        if (context is not null)
        {
            context.NewState[name] = "done";
        }

        if (FailAt == name)
        {
            throw new InvalidOperationException($"{name} failed");
        }

        return Task.CompletedTask;
    }
}
