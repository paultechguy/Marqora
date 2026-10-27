// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli.Reporting;

/// <summary>How much a run says on stderr (CLI-RPT-06).</summary>
internal enum Verbosity
{
    /// <summary>Errors and warnings only: no summary line per file.</summary>
    Quiet,

    Normal,

    /// <summary>Also timings, the settings and theme in force and where they came from, the base folder.</summary>
    Verbose,
}

/// <summary>When stderr is colored (CLI-RPT-07).</summary>
internal enum ColorMode
{
    /// <summary>Only when stderr is a terminal and NO_COLOR is not set.</summary>
    Auto,

    /// <summary>Always, NO_COLOR or not.</summary>
    Always,

    Never,
}

/// <summary>
/// Every message a person reads, and nothing a program reads: stderr only, so a pipe never
/// receives one (CLI-RPT-02). Each line starts "marqora-cli:", names the file it is about when
/// there is one, and says in a sentence what happened and, where there is something to do,
/// what.
///
/// The one door for messages. A source-scanning test fails on a write to the console anywhere
/// else, so what the CLI can say is the list of calls into this class.
/// </summary>
internal sealed class Reporter(TextWriter error, Verbosity verbosity, bool color)
{
    private const string Red = "\u001b[31m";
    private const string Yellow = "\u001b[33m";
    private const string Dim = "\u001b[2m";
    private const string Reset = "\u001b[0m";

    public Verbosity Verbosity => verbosity;

    public bool IsVerbose => verbosity == Verbosity.Verbose;

    /// <summary>Decides color the way --color says, from the run's own environment.</summary>
    public static bool UsesColor(ColorMode mode, CliEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return mode switch
        {
            ColorMode.Always => true,
            ColorMode.Never => false,
            _ => environment.ErrorIsTerminal && string.IsNullOrEmpty(environment.GetVariable("NO_COLOR")),
        };
    }

    /// <summary>Something went wrong with this file, or with the run when there is no file.</summary>
    public void Error(string? path, string message) => Write(path, "error", Red, message);

    /// <summary>Worth knowing, and the run carries on.</summary>
    public void Warning(string? path, string message) => Write(path, "warning", Yellow, message);

    /// <summary>
    /// A line that is the run's answer on stderr - "would change", under --check - and so is said
    /// even under --quiet, which hides only the routine.
    /// </summary>
    public void Notice(string? path, string message) => Write(path, null, Yellow, message);

    /// <summary>A line per file saying what was done to it. Quiet suppresses it.</summary>
    public void Summary(string? path, string message)
    {
        if (verbosity != Verbosity.Quiet)
        {
            Write(path, null, null, message);
        }
    }

    /// <summary>Detail for --verbose only.</summary>
    public void Detail(string? path, string message)
    {
        if (verbosity == Verbosity.Verbose)
        {
            Write(path, null, Dim, message);
        }
    }

    /// <summary>A finding about a file, from a verb whose result is the file rather than the findings.</summary>
    public void Finding(string path, DocumentFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        string? paint = finding.Severity switch
        {
            FindingSeverity.Error => Red,
            FindingSeverity.Warning => Yellow,
            _ => null,
        };

        // A compiler's order - where, then how bad, then what - which editors and CI log viewers
        // already know how to turn into a link to the line.
        error.WriteLine($"marqora-cli: {Locate(path, finding)}: {Paint(paint, finding.Severity.ToString().ToLowerInvariant() + ":")} {finding.Message} [{finding.Code}]");
    }

    /// <summary>"guide.md:12:5", "guide.md:12" or "guide.md", as much as the finding knows.</summary>
    public static string Locate(string path, DocumentFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        return finding.Line > 0
            ? finding.Column > 0 ? $"{path}:{finding.Line}:{finding.Column}" : $"{path}:{finding.Line}"
            : path;
    }

    /// <summary>A usage error: what is wrong with the command line, and where help is.</summary>
    public void Usage(string message, string helpCommand) =>
        error.WriteLine($"marqora-cli: {Paint(Red, "error:")} {message} See '{helpCommand} --help'.");

    private void Write(string? path, string? label, string? paint, string message)
    {
        string subject = path is null ? string.Empty : path + ": ";
        string prefix = label is null ? string.Empty : Paint(paint, label + ":") + " ";

        error.WriteLine(paint is not null && label is null
            ? $"marqora-cli: {Paint(paint, subject + message)}"
            : $"marqora-cli: {prefix}{subject}{message}");
    }

    private string Paint(string? code, string text) =>
        color && code is not null ? code + text + Reset : text;
}
