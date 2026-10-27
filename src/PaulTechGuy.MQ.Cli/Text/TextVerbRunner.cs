// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Inputs;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Services;

namespace PaulTechGuy.MQ.Cli.Text;

/// <summary>What a text verb made of one document.</summary>
/// <param name="Text">The document as the verb would leave it, in any line endings: the runner puts the file's back.</param>
/// <param name="Findings">What it noticed and wants said: an uncertain heading, a list left alone.</param>
/// <param name="Summary">A few words for the summary line when the file changed: "3 headings numbered".</param>
internal sealed record VerbResult(string Text, IReadOnlyList<DocumentFinding> Findings, string? Summary = null)
{
    /// <summary>
    /// Why the verb could do nothing with this document, when it could not - a regular expression
    /// that ran out of time. The file is left as it was and the run ends with exit 2: what was
    /// asked for cannot be done as asked.
    /// </summary>
    public string? Failure { get; init; }

    public static VerbResult Of(string text) => new(text, []);

    public static VerbResult Failed(string text, string failure) => new(text, []) { Failure = failure };
}

/// <summary>
/// The text verbs' one path from inputs to results (CLI-OUT-01 to CLI-OUT-06): format, number,
/// unnumber, replace, renumber-lists and diagram-layout differ only in what they do to a
/// document's text, and that is all each of them supplies.
///
/// <list type="bullet">
/// <item>No mode prints the result and touches nothing. Several inputs with no mode is refused,
/// because the documents would come out concatenated.</item>
/// <item>--write refuses every read-only input before anything is written, then rewrites each
/// file whose bytes would change, through <see cref="SafeFileWriter"/>, and leaves the rest alone
/// with their modification times.</item>
/// <item>--check lists the files that would change and exits 1 if there are any.</item>
/// <item>--dry-run prints a unified diff per file that would change.</item>
/// </list>
///
/// A run carries on past a file that fails, unless --fail-fast, and ends with the most severe
/// outcome it met (CLI-RPT-03).
/// </summary>
internal static class TextVerbRunner
{
    public static async Task<ExitCode> RunAsync(
        RunContext context,
        ParseResult parse,
        InputOptions inputs,
        ModeOptions modes,
        Func<Document, VerbResult> transform,
        bool verbOwnsEndings = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(transform);

        TextMode mode = CommandShared.ModeOf(parse, modes);
        (IReadOnlyList<InputFile> files, ExitCode worst) = Documents.Resolve(context, parse, inputs);

        if (worst != ExitCode.Success && context.FailFast)
        {
            return worst;
        }

        if (mode == TextMode.Print && files.Count > 1 && !context.Results.IsJson)
        {
            throw new UsageException(
                $"{files.Count} files were given, and printing them all would run them together. Add --write to rewrite each, or --check or --dry-run to see what would change.");
        }

        if (mode == TextMode.Write && files.Any(file => file.IsStandardInput))
        {
            throw new UsageException("--write needs files: standard input has nowhere to be written back. Leave out --write to print the result.");
        }

        if (mode == TextMode.Write && RefuseReadOnly(context, files))
        {
            return ExitCode.Input;
        }

        string? baseDir = parse.GetValue(inputs.BaseDir);

        for (int index = 0; index < files.Count; index++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            ExitCode code = await OneAsync(context, files[index], baseDir, mode, transform, verbOwnsEndings).ConfigureAwait(false);
            worst = ExitCodes.Worst(worst, code);

            if (context.FailFast && code is not (ExitCode.Success or ExitCode.Findings))
            {
                foreach (InputFile skipped in files.Skip(index + 1))
                {
                    context.Results.Json(new JsonLine { Path = skipped.Display, Verb = context.Verb, Outcome = Outcomes.Skipped });
                }

                break;
            }
        }

        return worst;
    }

    /// <summary>
    /// Every input checked before any is written: a read-only one among them stops the run with
    /// exit 3 and names each, and nothing is changed - not the writable ones either. The CLI's form
    /// of the app's rule that a command never fails part way on a read-only document (CLI-IN-05).
    /// </summary>
    private static bool RefuseReadOnly(RunContext context, IReadOnlyList<InputFile> files)
    {
        InputFile[] readOnly = [.. files.Where(file => File.GetAttributes(file.FullPath!).HasFlag(FileAttributes.ReadOnly))];

        foreach (InputFile file in readOnly)
        {
            const string Message = "is read-only, so --write would fail on it. Nothing was written. Make it writable, or leave it out.";

            context.Reporter.Error(file.Display, Message);
            context.Results.Json(new JsonLine { Path = file.Display, Verb = context.Verb, Outcome = Outcomes.Failed, Message = Message });
        }

        return readOnly.Length > 0;
    }

    private static async Task<ExitCode> OneAsync(
        RunContext context,
        InputFile input,
        string? baseDir,
        TextMode mode,
        Func<Document, VerbResult> transform,
        bool verbOwnsEndings)
    {
        if (await Documents.ReadAsync(context, input, baseDir).ConfigureAwait(false) is not { } document)
        {
            return ExitCode.Input;
        }

        VerbResult result = transform(document);

        if (result.Failure is { } failure)
        {
            context.Reporter.Error(input.Display, failure);
            context.Results.Json(new JsonLine { Path = input.Display, Verb = context.Verb, Outcome = Outcomes.Failed, Message = failure });
            return ExitCode.Usage;
        }

        string output = document.File.Restore(result.Text, verbOwnsEndings);
        bool changed = output != document.File.Text;
        string? baseDirForJson = input.IsStandardInput ? document.BaseFolder : null;

        if (!context.Results.IsJson)
        {
            foreach (DocumentFinding finding in result.Findings)
            {
                context.Reporter.Finding(input.Display, finding);
            }
        }

        JsonLine line = new() { Path = input.Display, Verb = context.Verb, Outcome = Outcomes.Unchanged, Findings = result.Findings, BaseDir = baseDirForJson };

        switch (mode)
        {
            case TextMode.Print:
                context.Results.Text(output);
                context.Results.Json(line with { Outcome = Outcomes.Printed, Text = output });
                return ExitCode.Success;

            case TextMode.Check:
                if (changed)
                {
                    context.Reporter.Notice(input.Display, "would change.");
                }

                context.Results.Json(line with { Outcome = changed ? Outcomes.WouldChange : Outcomes.Unchanged });
                return changed ? ExitCode.Findings : ExitCode.Success;

            case TextMode.DryRun:
                string diff = UnifiedDiff.Create(document.File.Text, output, input.Display);

                context.Results.Text(diff);
                context.Results.Json(line with { Outcome = changed ? Outcomes.WouldChange : Outcomes.Unchanged, Diff = changed ? diff : null });
                return ExitCode.Success;

            default:
                return await WriteAsync(context, document, output, changed, result, line).ConfigureAwait(false);
        }
    }

    private static async Task<ExitCode> WriteAsync(RunContext context, Document document, string output, bool changed, VerbResult result, JsonLine line)
    {
        string path = document.Input.FullPath!;

        if (!changed)
        {
            context.Reporter.Summary(document.Display, "unchanged.");
            context.Results.Json(line);
            return ExitCode.Success;
        }

        try
        {
            await SafeFileWriter.WriteAsync(path, document.File.Encode(output), new ReporterLogger(context.Reporter), cancellationToken: context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string message = $"Could not be written: {ex.Message}";

            context.Reporter.Error(document.Display, message);
            context.Results.Json(line with { Outcome = Outcomes.Failed, Message = message });
            return ExitCode.Output;
        }

        context.Reporter.Summary(document.Display, result.Summary is { } summary ? $"written ({summary})." : "written.");
        context.Results.Json(line with { Outcome = Outcomes.Written, Outputs = [document.Display] });
        return ExitCode.Success;
    }
}
