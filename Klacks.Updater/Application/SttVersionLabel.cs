// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// The OCI label the API image carries to pin the klacks-stt version, and the strict validation of its value: exactly
/// MAJOR.MINOR.PATCH in ASCII digits. Anything else (absent label, "no value" output, pre-release suffix, extra lines) is
/// treated as no pin.
/// </summary>
using System.Text.RegularExpressions;

namespace Klacks.Updater.Application;

public static class SttVersionLabel
{
    public const string Key = "org.klacks.stt-version";

    private const string VersionPattern = @"^[0-9]+\.[0-9]+\.[0-9]+\z";

    private static readonly Regex VersionRegex = new(VersionPattern, RegexOptions.CultureInvariant);

    public static string? Parse(string? rawValue)
    {
        var value = rawValue?.Trim();
        return !string.IsNullOrEmpty(value) && VersionRegex.IsMatch(value) ? value : null;
    }
}
