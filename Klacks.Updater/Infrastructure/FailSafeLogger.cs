// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Logger created by FailSafeLoggerProvider: forwards every call to the inner logger, but catches exceptions thrown
/// while writing an entry and hands them to the failure callback instead of letting them reach the caller.
/// </summary>
/// <param name="inner">Logger that writes the entries</param>
/// <param name="onWriteFailure">Called with the exception whenever the inner logger fails to write an entry</param>

namespace Klacks.Updater.Infrastructure;

public sealed class FailSafeLogger : ILogger
{
    private readonly ILogger _inner;
    private readonly Action<Exception> _onWriteFailure;

    public FailSafeLogger(ILogger inner, Action<Exception> onWriteFailure)
    {
        _inner = inner;
        _onWriteFailure = onWriteFailure;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return _inner.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _inner.IsEnabled(logLevel);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        try
        {
            _inner.Log(logLevel, eventId, state, exception, formatter);
        }
        catch (Exception writeFailure)
        {
            _onWriteFailure(writeFailure);
        }
    }
}
