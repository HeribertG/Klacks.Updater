// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Minimal async wrapper around an external process. Throws when the process exits non-zero so
/// callers (applier/backup) surface failures to the executor, which then rolls back. The failure message carries stderr,
/// or stdout when stderr is blank (sc.exe reports its errors on stdout), trimmed and truncated.
/// </summary>
using System.Diagnostics;

namespace Klacks.Updater.Infrastructure;

public static class ProcessRunner
{
    private const int MaxFailureOutputLength = 2000;

    private const string TruncationSuffix = "...";

    public static async Task<string> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildFailureMessage(fileName, arguments, process.ExitCode, stdout, stderr));
        }

        return stdout;
    }

    public static async Task RunWithStdinAsync(
        string fileName,
        string arguments,
        string stdin,
        CancellationToken cancellationToken = default,
        IDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildFailureMessage(fileName, arguments, process.ExitCode, string.Empty, stderr));
        }
    }

    public static string BuildFailureMessage(string fileName, string arguments, int exitCode, string stdout, string stderr)
    {
        var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        var trimmed = detail.Trim();
        if (trimmed.Length > MaxFailureOutputLength)
        {
            trimmed = trimmed[..MaxFailureOutputLength] + TruncationSuffix;
        }

        return $"'{fileName} {arguments}' exited {exitCode}: {trimmed}";
    }
}
