// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Minimal async wrapper around an external process. Throws when the process exits non-zero so
/// callers (applier/backup) surface failures to the executor, which then rolls back. The failure message carries stderr,
/// or stdout when stderr is blank (sc.exe reports its errors on stdout), trimmed and truncated. Callers pass either a
/// raw argument string or an argument list; the list form is escaped by the runtime and is the one to use whenever an
/// argument comes from configuration or contains spaces.
/// </summary>
using System.Diagnostics;

namespace Klacks.Updater.Infrastructure;

public static class ProcessRunner
{
    private const int MaxFailureOutputLength = 2000;

    private const string TruncationSuffix = "...";

    private const char ArgumentSeparator = ' ';

    public static Task<string> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo(fileName, workingDirectory, environment);
        startInfo.RedirectStandardOutput = true;
        startInfo.Arguments = arguments;
        return ExecuteAsync(startInfo, arguments, cancellationToken);
    }

    public static Task<string> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = CreateStartInfo(fileName, workingDirectory, environment);
        startInfo.RedirectStandardOutput = true;
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return ExecuteAsync(startInfo, FormatArguments(arguments), cancellationToken);
    }

    public static Task RunWithStdinAsync(
        string fileName,
        string arguments,
        string stdin,
        CancellationToken cancellationToken = default,
        IDictionary<string, string>? environment = null)
    {
        var startInfo = CreateStartInfo(fileName, null, environment);
        startInfo.Arguments = arguments;
        return ExecuteWithStdinAsync(startInfo, arguments, stdin, cancellationToken);
    }

    public static Task RunWithStdinAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string stdin,
        CancellationToken cancellationToken = default,
        IDictionary<string, string>? environment = null)
    {
        var startInfo = CreateStartInfo(fileName, null, environment);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return ExecuteWithStdinAsync(startInfo, FormatArguments(arguments), stdin, cancellationToken);
    }

    public static string FormatArguments(IReadOnlyList<string> arguments) =>
        string.Join(ArgumentSeparator, arguments);

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

    private static ProcessStartInfo CreateStartInfo(
        string fileName, string? workingDirectory, IDictionary<string, string>? environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
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

        return startInfo;
    }

    private static async Task<string> ExecuteAsync(
        ProcessStartInfo startInfo, string displayArguments, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                BuildFailureMessage(startInfo.FileName, displayArguments, process.ExitCode, stdout, stderr));
        }

        return stdout;
    }

    private static async Task ExecuteWithStdinAsync(
        ProcessStartInfo startInfo, string displayArguments, string stdin, CancellationToken cancellationToken)
    {
        startInfo.RedirectStandardInput = true;

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                BuildFailureMessage(startInfo.FileName, displayArguments, process.ExitCode, string.Empty, stderr));
        }
    }
}
