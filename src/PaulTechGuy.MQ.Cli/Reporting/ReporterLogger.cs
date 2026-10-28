// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace PaulTechGuy.MQ.Cli.Reporting;

/// <summary>
/// The libraries' log, through the reporter: their warnings and errors as warnings, the rest as
/// --verbose detail. The libraries were written for the app's log file and say things in its
/// voice; on a command line only the ones that matter are worth a line by default.
/// </summary>
internal class ReporterLogger(Reporter reporter) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel >= LogLevel.Warning || (reporter.IsVerbose && logLevel >= LogLevel.Information);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);

        if (logLevel >= LogLevel.Warning)
        {
            reporter.Warning(null, message);
        }
        else
        {
            reporter.Detail(null, message);
        }
    }
}

/// <summary>The same, for a library that takes a typed logger.</summary>
internal sealed class ReporterLogger<T>(Reporter reporter) : ReporterLogger(reporter), ILogger<T>;
