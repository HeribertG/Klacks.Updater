// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Raw-Npgsql access to the ASSISTANT_TTS_LOCAL_BASE_URL row of the settings table (unique by type, same upsert pattern as
/// the other updater-written settings). The row exists exactly when local speech output is installed; the updater writes
/// it only after the speech model is actually in place.
/// </summary>
/// <param name="options">Deployment configuration (connection string)</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Klacks.Updater.Infrastructure;

public class NpgsqlLocalTtsStore : ILocalTtsStore
{
    private readonly UpdaterOptions _options;

    public NpgsqlLocalTtsStore(IOptions<UpdaterOptions> options)
    {
        _options = options.Value;
    }

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT value FROM settings WHERE type = @type LIMIT 1;", connection);
        command.Parameters.AddWithValue("type", TtsPluginConstants.LocalBaseUrlSettingKey);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is string value && !string.IsNullOrWhiteSpace(value);
    }

    public async Task UpsertBaseUrlAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "INSERT INTO settings (id, type, value) VALUES (gen_random_uuid(), @type, @value) ON CONFLICT (type) DO UPDATE SET value = EXCLUDED.value;",
            connection);
        command.Parameters.AddWithValue("type", TtsPluginConstants.LocalBaseUrlSettingKey);
        command.Parameters.AddWithValue("value", baseUrl);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
