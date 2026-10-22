// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Raw-Npgsql upsert of the well-known self-hosted STT row in custom_stt_providers (same row as
/// Klacks.Api/deploy/whisper-stt-provider.sql). Name, URL and model follow the installed engine (Whisper or Parakeet).
/// The updater writes it only after the engine and model are actually in place so the API never lists a dead provider.
/// </summary>
/// <param name="options">Deployment configuration (connection string)</param>
using Klacks.Updater.Application;
using Klacks.Updater.Domain;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Klacks.Updater.Infrastructure;

public class NpgsqlWhisperProviderStore : IWhisperProviderStore
{
    private readonly UpdaterOptions _options;

    public NpgsqlWhisperProviderStore(IOptions<UpdaterOptions> options)
    {
        _options = options.Value;
    }

    public async Task UpsertEnabledAsync(string modelId, string providerName, string apiUrl, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            INSERT INTO custom_stt_providers
                (id, name, connection_type, api_url, api_key, language_model, is_enabled, is_system, is_deleted, create_time)
            VALUES
                (@id, @name, @connectionType, @apiUrl, NULL, @model, true, true, false, now())
            ON CONFLICT (id) DO UPDATE
            SET name = EXCLUDED.name,
                api_url = EXCLUDED.api_url,
                language_model = EXCLUDED.language_model,
                is_enabled = true,
                is_deleted = false,
                update_time = now();";

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", WhisperPluginConstants.ProviderId);
        command.Parameters.AddWithValue("name", providerName);
        command.Parameters.AddWithValue("connectionType", WhisperPluginConstants.ConnectionTypeRest);
        command.Parameters.AddWithValue("apiUrl", apiUrl);
        command.Parameters.AddWithValue("model", modelId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE custom_stt_providers SET is_enabled = false, update_time = now() WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", WhisperPluginConstants.ProviderId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
