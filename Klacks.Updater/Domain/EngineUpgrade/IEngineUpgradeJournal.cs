// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain.EngineUpgrade;

/// <summary>
/// Durable journal of the one engine upgrade that may be open at a time. Saving replaces the journal atomically;
/// archiving moves it aside once the update_history row carries the terminal status.
/// </summary>
public interface IEngineUpgradeJournal
{
    bool Exists();

    Task<EngineUpgradeJournalEntry?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(EngineUpgradeJournalEntry entry, CancellationToken cancellationToken);

    Task ArchiveAsync(EngineUpgradeJournalEntry entry, CancellationToken cancellationToken);
}
