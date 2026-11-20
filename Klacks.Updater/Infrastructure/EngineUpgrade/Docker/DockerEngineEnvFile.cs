// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

/// <summary>
/// Pure transformations of the compose .env lines for the engine upgrade. A key is read as its raw value (everything
/// after the first '=') or DockerEngineStateKeys.Missing when it is absent; applying Missing removes the key again, so a
/// revert reproduces the original file. Other lines keep their position. Values with line breaks are refused because
/// they would inject further .env entries; IsRestorableRawValue limits values taken from the journal to image and folder
/// characters (plus quotes and blanks), so a tampered journal cannot write variable expansions into .env.
/// </summary>
public static partial class DockerEngineEnvFile
{
    private const char KeyValueSeparator = '=';
    private static readonly char[] LineBreaks = ['\r', '\n'];

    public static bool IsRestorableRawValue(string rawValue) =>
        rawValue == DockerEngineStateKeys.Missing || RawValuePattern().IsMatch(rawValue);

    public static int CountOccurrences(string[] lines, string key) => lines.Count(line => IsKeyLine(line, key));

    public static string ReadRaw(string[] lines, string key)
    {
        var line = lines.LastOrDefault(l => IsKeyLine(l, key));
        return line is null ? DockerEngineStateKeys.Missing : line.TrimStart()[(key.Length + 1)..];
    }

    public static string[] Apply(string[] lines, string key, string rawValueOrMissing)
    {
        if (rawValueOrMissing == DockerEngineStateKeys.Missing)
        {
            return lines.Where(line => !IsKeyLine(line, key)).ToArray();
        }

        if (rawValueOrMissing.IndexOfAny(LineBreaks) >= 0)
        {
            throw new InvalidOperationException($"Refusing to write a line break into the .env value of {key}.");
        }

        var entry = key + KeyValueSeparator + rawValueOrMissing;
        var result = new List<string>(lines.Length + 1);
        var written = false;
        foreach (var line in lines)
        {
            if (!IsKeyLine(line, key))
            {
                result.Add(line);
            }
            else if (!written)
            {
                result.Add(entry);
                written = true;
            }
        }

        if (!written)
        {
            result.Add(entry);
        }

        return result.ToArray();
    }

    private static bool IsKeyLine(string line, string key) =>
        line.TrimStart().StartsWith(key + KeyValueSeparator, StringComparison.Ordinal);

    [GeneratedRegex(@"^[A-Za-z0-9._:/@""' -]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex RawValuePattern();
}
