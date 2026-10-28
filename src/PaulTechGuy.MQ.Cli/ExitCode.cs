// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli;

/// <summary>
/// What a run tells its caller, and nothing else does. Fixed once released (CLI-RPT-01): a script
/// branches on these numbers, so a meaning never moves. docs/CLI.md carries the same table, and a
/// test holds the two together.
/// </summary>
internal enum ExitCode
{
    /// <summary>Everything asked for was done.</summary>
    Success = 0,

    /// <summary>The run worked and found something: check's problems, or a file --check says would change.</summary>
    Findings = 1,

    /// <summary>The command line itself: an unknown verb or option, conflicting flags, an unknown theme.</summary>
    Usage = 2,

    /// <summary>An input: missing, unreadable, not UTF-8, read-only under --write, or two that would collide.</summary>
    Input = 3,

    /// <summary>An output: it cannot be written, or it exists and --overwrite was not given.</summary>
    Output = 4,

    /// <summary>The browser: WebView2 missing, a render that timed out, the PDF engine failing.</summary>
    Render = 5,

    /// <summary>Ctrl+C or Ctrl+Break.</summary>
    Canceled = 130,
}

internal static class ExitCodes
{
    /// <summary>
    /// The more severe of two, for a run over several files that met more than one outcome
    /// (CLI-RPT-03): canceled, then render, output, input, usage, findings, success.
    /// </summary>
    public static ExitCode Worst(ExitCode first, ExitCode second) =>
        Rank(first) >= Rank(second) ? first : second;

    private static int Rank(ExitCode code) => code == ExitCode.Canceled ? int.MaxValue : (int)code;
}
