// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

using System.Text.RegularExpressions;

namespace Klacks.Updater.Infrastructure.EngineUpgrade.Docker;

/// <summary>
/// Pure helpers for the postgres image reference of the engine upgrade: the repository of a reference (tag and digest
/// stripped, a registry port is kept), the OCI digest format and the pinned form repo@sha256:... that goes into .env.
/// </summary>
public static partial class PostgresImageReference
{
    private const char DigestSeparator = '@';
    private const char TagSeparator = ':';
    private const char PathSeparator = '/';

    public static string Repository(string reference)
    {
        var withoutDigest = reference.Split(DigestSeparator)[0];
        var lastSlash = withoutDigest.LastIndexOf(PathSeparator);
        var tagColon = withoutDigest.LastIndexOf(TagSeparator);
        return tagColon > lastSlash ? withoutDigest[..tagColon] : withoutDigest;
    }

    public static bool IsDigest(string? digest) => digest is not null && DigestPattern().IsMatch(digest);

    public static string Pin(string repository, string digest) => repository + DigestSeparator + digest;

    public static bool IsPinnedFor(string? image, string repository) =>
        image is not null
        && image.StartsWith(repository + DigestSeparator, StringComparison.Ordinal)
        && IsDigest(image[(repository.Length + 1)..]);

    [GeneratedRegex(@"^sha256:[0-9a-f]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}
