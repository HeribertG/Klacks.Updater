// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Replaces a text file without ever exposing a truncated version: the content is written to a temporary file in the
/// same directory and moved over the target. The unix permission bits of an existing target are carried over so a
/// restricted file (compose .env) does not become world-readable.
/// </summary>

namespace Klacks.Updater.Infrastructure;

public static class AtomicFile
{
    private const string TemporarySuffix = ".updater-tmp";

    public static async Task WriteAllLinesAsync(string path, IEnumerable<string> lines, CancellationToken cancellationToken = default)
    {
        var temporaryPath = path + TemporarySuffix;
        try
        {
            await File.WriteAllLinesAsync(temporaryPath, lines, cancellationToken);
            if (!OperatingSystem.IsWindows() && File.Exists(path))
            {
                File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(path));
            }

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
