// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Inputs;
using PaulTechGuy.MQ.Cli.Reporting;

namespace PaulTechGuy.MQ.Cli.Text;

/// <summary>What a report verb found in one document.</summary>
/// <param name="Lines">What it prints for the document, a line each.</param>
/// <param name="Findings">What it found, for the JSON.</param>
/// <param name="Fails">Whether this document fails the run: check's errors.</param>
internal sealed record Report(IReadOnlyList<string> Lines, IReadOnlyList<DocumentFinding> Findings, bool Fails)
{
    /// <summary>The JSON line to write in place of the default, when the verb has more to say: outline's headings.</summary>
    public Func<JsonLine, JsonLine>? Json { get; init; }
}

/// <summary>
/// The verbs that read documents and say something about them rather than rewrite them: check,
/// toc and outline. Several inputs are fine (CLI-OUT-02); each document's lines are printed as a
/// block, headed by its name when there is more than one. A document that fails makes the run exit
/// 1, after the rest have been read.
/// </summary>
internal static class ReportVerbRunner
{
    public static async Task<ExitCode> RunAsync(RunContext context, ParseResult parse, InputOptions inputs, Func<Document, Report> report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(report);

        (IReadOnlyList<InputFile> files, ExitCode worst) = Documents.Resolve(context, parse, inputs);

        if (worst != ExitCode.Success && context.FailFast)
        {
            return worst;
        }

        string? baseDir = parse.GetValue(inputs.BaseDir);
        bool headed = files.Count > 1;

        foreach (InputFile input in files)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            if (await Documents.ReadAsync(context, input, baseDir).ConfigureAwait(false) is not { } document)
            {
                worst = ExitCodes.Worst(worst, ExitCode.Input);

                if (context.FailFast)
                {
                    break;
                }

                continue;
            }

            Report result = report(document);

            if (headed && result.Lines.Count > 0)
            {
                context.Results.Line($"{input.Display}:");
            }

            foreach (string printed in result.Lines)
            {
                context.Results.Line(printed);
            }

            if (headed && result.Lines.Count > 0)
            {
                context.Results.Line(string.Empty);
            }

            var line = new JsonLine
            {
                Path = input.Display,
                Verb = context.Verb,
                Outcome = result.Fails ? Outcomes.Found : Outcomes.Passed,
                Findings = result.Findings,
                BaseDir = input.IsStandardInput ? document.BaseFolder : null,
            };

            context.Results.Json(result.Json is { } shape ? shape(line) : line);

            if (result.Fails)
            {
                worst = ExitCodes.Worst(worst, ExitCode.Findings);
            }
        }

        return worst;
    }
}
