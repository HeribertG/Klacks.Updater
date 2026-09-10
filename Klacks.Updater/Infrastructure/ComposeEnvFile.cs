// Copyright (c) Heribert Gasparoli Private. All rights reserved.

/// <summary>
/// Pure line transformations for the compose .env file: the COMPOSE_PROFILES entry (so an installed
/// profile survives manual `docker compose up` runs) and single KEY=value entries such as the image
/// tags the applier activates (so a reboot or a manual `docker compose up` resolves the version that
/// is actually running). Other lines are preserved; an unchanged file is returned as the same array.
/// </summary>
namespace Klacks.Updater.Infrastructure;

public static class ComposeEnvFile
{
    private const string ComposeProfilesPrefix = "COMPOSE_PROFILES=";
    private const char KeyValueSeparator = '=';

    public static string[] WithValue(string[] lines, string key, string value)
    {
        var entry = $"{key}{KeyValueSeparator}{value}";
        var index = FindKeyLine(lines, key);
        if (index < 0)
        {
            return [.. lines, entry];
        }

        if (string.Equals(lines[index].TrimStart(), entry, StringComparison.Ordinal))
        {
            return lines;
        }

        var result = (string[])lines.Clone();
        result[index] = entry;
        return result;
    }

    private static int FindKeyLine(string[] lines, string key)
    {
        var prefix = $"{key}{KeyValueSeparator}";
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith(prefix, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public static string[] WithProfile(string[] lines, string profile)
    {
        var index = FindProfilesLine(lines);
        if (index < 0)
        {
            return [.. lines, $"{ComposeProfilesPrefix}{profile}"];
        }

        var tokens = ParseTokens(lines[index]);
        if (tokens.Contains(profile, StringComparer.OrdinalIgnoreCase))
        {
            return lines;
        }

        tokens.Add(profile);
        var result = (string[])lines.Clone();
        result[index] = $"{ComposeProfilesPrefix}{string.Join(',', tokens)}";
        return result;
    }

    public static string[] WithoutProfile(string[] lines, string profile)
    {
        var index = FindProfilesLine(lines);
        if (index < 0)
        {
            return lines;
        }

        var tokens = ParseTokens(lines[index]);
        var remaining = tokens.Where(t => !string.Equals(t, profile, StringComparison.OrdinalIgnoreCase)).ToList();
        if (remaining.Count == tokens.Count)
        {
            return lines;
        }

        if (remaining.Count == 0)
        {
            return lines.Where((_, i) => i != index).ToArray();
        }

        var result = (string[])lines.Clone();
        result[index] = $"{ComposeProfilesPrefix}{string.Join(',', remaining)}";
        return result;
    }

    private static int FindProfilesLine(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith(ComposeProfilesPrefix, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> ParseTokens(string line)
    {
        var value = line.TrimStart()[ComposeProfilesPrefix.Length..];
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
