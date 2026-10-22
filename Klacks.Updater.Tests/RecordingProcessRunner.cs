// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Test double for IProcessRunner: records every call (arguments joined by spaces, environment snapshot) and answers via
/// a handler that may return stdout or throw to simulate a failing process.
/// </summary>
namespace Klacks.Updater.Tests;

using Klacks.Updater.Domain;

public sealed class RecordingProcessRunner : IProcessRunner
{
    private readonly Func<string, string> _handler;

    public RecordingProcessRunner(Func<string, string>? handler = null)
    {
        _handler = handler ?? (_ => string.Empty);
    }

    public List<string> Calls { get; } = [];

    public List<IReadOnlyDictionary<string, string>> Environments { get; } = [];

    public Task<string> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var call = string.Join(' ', arguments);
        Calls.Add(call);
        Environments.Add(environment is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(environment));
        return Task.FromResult(_handler(call));
    }
}
