// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Production IProcessRunner that delegates to ProcessRunner.
/// </summary>
using Klacks.Updater.Domain;

namespace Klacks.Updater.Infrastructure;

public class SystemProcessRunner : IProcessRunner
{
    public Task<string> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        return ProcessRunner.RunAsync(fileName, arguments, workingDirectory, environment, cancellationToken);
    }
}
