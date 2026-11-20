// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Replaces a text file without ever exposing a truncated version: the content is written to a temporary file in the
/// same directory and moved over the target. On unix the temporary file is created owner-only (0600) before any content
/// is written, so secrets of a compose .env are never readable by others, and the permission bits of an existing target are
/// applied before the move.
/// </summary>

using System.Text;

namespace Klacks.Updater.Infrastructure;

public static class AtomicFile
{
    private const string TemporarySuffix = ".updater-tmp";
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static async Task WriteAllLinesAsync(string path, IEnumerable<string> lines, CancellationToken cancellationToken = default)
    {
        var temporaryPath = path + TemporarySuffix;
        try
        {
            File.Delete(temporaryPath);
            await using (var writer = new StreamWriter(new FileStream(temporaryPath, CreateOptions()), new UTF8Encoding(false)))
            {
                foreach (var line in lines)
                {
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
                }
            }

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

    private static FileStreamOptions CreateOptions()
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnly;
        }

        return options;
    }
}
