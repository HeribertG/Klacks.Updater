// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

namespace Klacks.Updater.Domain;

/// <summary>
/// Runs an external process and returns its standard output; throws when the process exits non-zero.
/// Abstraction over the process runner so Docker CLI orchestration can be tested without Docker.
/// </summary>
public interface IProcessRunner
{
    Task<string> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default);
}
