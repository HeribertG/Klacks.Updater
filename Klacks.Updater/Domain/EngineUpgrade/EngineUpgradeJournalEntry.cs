// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

/// <summary>
/// Content of the engine upgrade journal (frozen field set). Old and New are the target state maps of the
/// EngineUpgradeContext and must never contain secrets; Timestamps holds the UTC time each phase was reached.
/// </summary>
public class EngineUpgradeJournalEntry
{
    public int Schema { get; set; }

    public Guid OperationId { get; set; }

    public string Mode { get; set; } = string.Empty;

    public int FromMajor { get; set; }

    public int ToMajor { get; set; }

    public EngineUpgradePhase Phase { get; set; }

    public Dictionary<string, string> Old { get; set; } = [];

    public Dictionary<string, string> New { get; set; } = [];

    public List<string> Databases { get; set; } = [];

    public List<EngineDump> Dumps { get; set; } = [];

    public string AppVersion { get; set; } = string.Empty;

    public string? Outcome { get; set; }

    public string? Message { get; set; }

    public Dictionary<string, DateTime> Timestamps { get; set; } = [];
}
