// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Computes the per-service security identifier Windows derives from a service name (S-1-5-80 followed by the five
/// little-endian DWORDs of the SHA-1 of the upper-cased UTF-16 name), so the NT SERVICE\name account can be granted
/// rights without asking the local security authority.
/// </summary>

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Klacks.Updater.Infrastructure;

public static class ServiceSid
{
    private const string Prefix = "S-1-5-80";
    private const int SubAuthorityCount = 5;
    private const int BytesPerSubAuthority = sizeof(uint);

    public static string FromName(string serviceName)
    {
        var hash = SHA1.HashData(Encoding.Unicode.GetBytes(serviceName.ToUpperInvariant()));
        var builder = new StringBuilder(Prefix);
        for (var i = 0; i < SubAuthorityCount; i++)
        {
            var subAuthority = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(i * BytesPerSubAuthority, BytesPerSubAuthority));
            builder.Append('-').Append(subAuthority);
        }

        return builder.ToString();
    }
}
