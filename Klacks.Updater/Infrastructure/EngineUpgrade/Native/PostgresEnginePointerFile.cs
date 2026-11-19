// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Reads and atomically writes the engine pointer file and the PostgresBinDirectory entry of klacks.native.json. A
/// missing pointer means the legacy layout; an unknown schema version or an incomplete pointer is refused. Writes go through a temporary file in the same directory that is moved over
/// the target, so a crash never leaves a half-written file.
/// </summary>
using System.Text.Json;
using System.Text.Json.Nodes;
using Klacks.Updater.Application;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Native;

public static class PostgresEnginePointerFile
{
    private const string TemporarySuffix = ".updater-tmp";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static PostgresEnginePointer? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var pointer = JsonSerializer.Deserialize<PostgresEnginePointer>(File.ReadAllText(path), SerializerOptions)
            ?? throw new InvalidDataException($"Engine pointer {path} is empty.");
        if (pointer.SchemaVersion != PostgresEnginePointer.CurrentSchemaVersion || pointer.Major <= 0
            || string.IsNullOrWhiteSpace(pointer.PostgresDirectory) || string.IsNullOrWhiteSpace(pointer.DataDirectory))
        {
            throw new InvalidDataException($"Engine pointer {path} has an unsupported schema or is incomplete.");
        }

        return pointer;
    }

    public static void Write(string path, PostgresEnginePointer pointer) =>
        WriteAtomically(path, JsonSerializer.Serialize(pointer, SerializerOptions));

    public static string? ReadRaw(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    public static void WriteRaw(string path, string content) => WriteAtomically(path, content);

    public static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public static string? ReadPostgresBinDirectory(string nativeConfigPath)
    {
        var root = JsonNode.Parse(File.ReadAllText(nativeConfigPath));
        return root?[UpdaterOptions.SectionName]?[nameof(UpdaterOptions.PostgresBinDirectory)]?.GetValue<string>();
    }

    public static void WritePostgresBinDirectory(string nativeConfigPath, string binDirectory)
    {
        var root = JsonNode.Parse(File.ReadAllText(nativeConfigPath))?.AsObject()
            ?? throw new InvalidDataException($"{nativeConfigPath} is not a JSON object.");
        if (root[UpdaterOptions.SectionName] is not JsonObject updater)
        {
            updater = new JsonObject();
            root[UpdaterOptions.SectionName] = updater;
        }

        updater[nameof(UpdaterOptions.PostgresBinDirectory)] = binDirectory;
        WriteAtomically(nativeConfigPath, root.ToJsonString(SerializerOptions));
    }

    private static void WriteAtomically(string path, string content)
    {
        var temporaryPath = path + TemporarySuffix;
        try
        {
            File.WriteAllText(temporaryPath, content);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
