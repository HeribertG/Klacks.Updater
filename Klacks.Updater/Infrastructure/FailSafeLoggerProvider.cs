// Copyright (c) Heribert Gasparoli. SPDX-License-Identifier: AGPL-3.0-only

/// <summary>
/// Decorates a logger provider so that a failing log sink can never change the program flow: write errors of the
/// inner loggers are swallowed instead of surfacing as an AggregateException at the call site. The first failure is
/// reported once to the given error output; later failures stay silent. Used for the Windows event log, whose writes
/// can fail with "Access is denied" while a freshly created event source is not yet usable.
/// </summary>
/// <param name="inner">Logger provider whose loggers do the actual writing</param>
/// <param name="errorOutput">Receives the one-time notice about the first swallowed write failure</param>

namespace Klacks.Updater.Infrastructure;

public sealed class FailSafeLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private const string WriteFailureNotice =
        "A log write through {0} failed and was ignored; further failures of this logger are not reported. {1}";

    private readonly ILoggerProvider _inner;
    private readonly TextWriter _errorOutput;
    private int _failureReported;

    public FailSafeLoggerProvider(ILoggerProvider inner, TextWriter errorOutput)
    {
        _inner = inner;
        _errorOutput = errorOutput;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FailSafeLogger(_inner.CreateLogger(categoryName), ReportWriteFailure);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        if (_inner is ISupportExternalScope scopeAware)
        {
            scopeAware.SetScopeProvider(scopeProvider);
        }
    }

    public void Dispose()
    {
        _inner.Dispose();
    }

    private void ReportWriteFailure(Exception exception)
    {
        if (Interlocked.Exchange(ref _failureReported, 1) != 0)
        {
            return;
        }

        try
        {
            _errorOutput.WriteLine(WriteFailureNotice, _inner.GetType().Name, exception);
        }
        catch (Exception)
        {
        }
    }
}
