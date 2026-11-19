// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Engine upgrade journal as a JSON file at &lt;BackupDirectory&gt;/engine-upgrade/journal.json. Every save writes a
/// temporary file in the same directory, flushes it to disk and moves it over the journal, so a crash never leaves a
/// half-written journal. Archiving moves it to journal-&lt;operationId&gt;.json next to it (an older archive of the
/// same operation is replaced). The phase is stored by name. Loading refuses an unknown schema or inconsistent majors;
/// the directory inherits the access rules of the backup directory (SYSTEM/Administrators on Windows, root in Docker).
/// </summary>
/// <param name="options">Updater configuration (backup directory)</param>
using System.Text.Json;
using System.Text.Json.Serialization;
using Klacks.Updater.Application;
using Klacks.Updater.Application.EngineUpgrade;
using Klacks.Updater.Domain.EngineUpgrade;
using Microsoft.Extensions.Options;

namespace Klacks.Updater.Infrastructure.EngineUpgrade;

public class FileEngineUpgradeJournal : IEngineUpgradeJournal
{
    private const string TemporarySuffix = ".tmp";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;

    public FileEngineUpgradeJournal(IOptions<UpdaterOptions> options)
    {
        _directory = Path.Combine(options.Value.BackupDirectory, UpdaterConstants.EngineUpgradeDirectoryName);
    }

    public string JournalPath => Path.Combine(_directory, EngineUpgradeConstants.JournalFileName);

    public bool Exists() => File.Exists(JournalPath);

    public async Task<EngineUpgradeJournalEntry?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!Exists())
        {
            return null;
        }

        await using var stream = File.OpenRead(JournalPath);
        var entry = await JsonSerializer.DeserializeAsync<EngineUpgradeJournalEntry>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidDataException($"Engine upgrade journal {JournalPath} is empty.");
        if (entry.Schema != EngineUpgradeConstants.JournalSchema || entry.OperationId == Guid.Empty || entry.ToMajor <= entry.FromMajor)
        {
            throw new InvalidDataException($"Engine upgrade journal {JournalPath} has an unsupported schema or inconsistent content.");
        }

        return entry;
    }

    public async Task SaveAsync(EngineUpgradeJournalEntry entry, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        var temporaryPath = JournalPath + TemporarySuffix;
        File.Delete(temporaryPath);
        await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, entry, SerializerOptions, CancellationToken.None);
            await stream.FlushAsync(CancellationToken.None);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporaryPath, JournalPath, overwrite: true);
    }

    public Task ArchiveAsync(EngineUpgradeJournalEntry entry, CancellationToken cancellationToken)
    {
        if (Exists())
        {
            var archivePath = Path.Combine(_directory, string.Format(EngineUpgradeConstants.ArchivedJournalFilePattern, entry.OperationId));
            File.Move(JournalPath, archivePath, overwrite: true);
        }

        return Task.CompletedTask;
    }
}
