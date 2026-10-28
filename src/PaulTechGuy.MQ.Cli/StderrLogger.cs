// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace PaulTechGuy.MQ.Cli;

/// <summary>
/// The libraries' log lines, to stderr and never stdout, so a pipe only ever receives a result.
/// The spike's whole reporting; WP-M1-02's Reporter takes over for everything a user reads.
/// </summary>
internal class StderrLogger(LogLevel minimum) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimum && logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        string level = logLevel switch
        {
            LogLevel.Trace => "trace",
            LogLevel.Debug => "debug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warning",
            LogLevel.Error => "error",
            _ => "critical",
        };

        Console.Error.WriteLine($"marqora-cli: {level}: {formatter(state, exception)}");

        if (exception is not null && minimum <= LogLevel.Debug)
        {
            Console.Error.WriteLine(exception);
        }
    }
}

/// <summary>The same, for the libraries that take a typed logger.</summary>
internal sealed class StderrLogger<T>(LogLevel minimum) : StderrLogger(minimum), ILogger<T>;
