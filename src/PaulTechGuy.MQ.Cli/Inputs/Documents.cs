// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Reporting;

namespace PaulTechGuy.MQ.Cli.Inputs;

/// <summary>A markdown document a verb works on: where it came from, its text, and the folder its relative links resolve against.</summary>
internal sealed record Document(InputFile Input, TextFile File, string BaseFolder)
{
    public string Display => Input.Display;

    /// <summary>
    /// A path for the document's own location, which the libraries use to resolve relative links:
    /// the file itself, or for standard input a name in the base folder that stands in for one.
    /// </summary>
    public string LocationPath => Input.FullPath ?? Path.Combine(BaseFolder, "stdin.md");
}

/// <summary>Turning a verb's inputs into documents, reporting each that cannot be one.</summary>
internal static class Documents
{
    /// <summary>
    /// The inputs as files, with every name or pattern that found nothing reported as an input
    /// error. The exit code is <see cref="ExitCode.Input"/> when anything was reported, so the run
    /// ends with it even if the files that were found all succeed (CLI-IN-01, CLI-IN-06).
    /// </summary>
    public static (IReadOnlyList<InputFile> Files, ExitCode Code) Resolve(RunContext context, ParseResult parse, InputOptions inputs, InputOrder order = InputOrder.Sorted)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(inputs);

        string[] given = parse.GetValue(inputs.Inputs) ?? [];
        (IReadOnlyList<InputFile> files, IReadOnlyList<InputProblem> problems) = InputSet.Resolve(given, context.Environment.CurrentDirectory, order);

        foreach (InputProblem problem in problems)
        {
            context.Reporter.Error(problem.Given, problem.Message);
            context.Results.Json(new JsonLine { Path = problem.Given, Verb = context.Verb, Outcome = Outcomes.Failed, Message = problem.Message });
        }

        return (files, problems.Count > 0 ? ExitCode.Input : ExitCode.Success);
    }

    /// <summary>
    /// One input read as a document, or null with the reason reported when it cannot be: missing by
    /// now, unreadable, or not UTF-8. Standard input resolves its links against --base-dir, or the
    /// current folder (CLI-IN-03).
    /// </summary>
    public static async Task<Document?> ReadAsync(RunContext context, InputFile input, string? baseDir)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            byte[] bytes;
            string baseFolder;

            if (input.IsStandardInput)
            {
                using Stream stdin = context.Environment.OpenStandardInput();
                using var copy = new MemoryStream();

                await stdin.CopyToAsync(copy, context.CancellationToken).ConfigureAwait(false);

                bytes = copy.ToArray();
                baseFolder = context.FullPath(baseDir ?? ".");

                context.Reporter.Detail("-", $"Relative links resolve against {baseFolder}.");
            }
            else
            {
                bytes = await File.ReadAllBytesAsync(input.FullPath!, context.CancellationToken).ConfigureAwait(false);
                baseFolder = Path.GetDirectoryName(input.FullPath!)!;
            }

            var document = new Document(input, TextFile.Decode(bytes), baseFolder);

            if (document.File.IsMixed)
            {
                context.Reporter.Detail(input.Display, $"Mixed line endings; any rewrite uses {(document.File.Ending == "\r\n" ? "CRLF" : "LF")}, the ending most lines have.");
            }

            return document;
        }
        catch (NotUtf8Exception ex)
        {
            return Fail(context, input, ex.Message);
        }
        catch (FileNotFoundException)
        {
            return Fail(context, input, "No such file.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(context, input, $"Could not be read: {ex.Message}");
        }
    }

    private static Document? Fail(RunContext context, InputFile input, string message)
    {
        context.Reporter.Error(input.Display, message);
        context.Results.Json(new JsonLine { Path = input.Display, Verb = context.Verb, Outcome = Outcomes.Failed, Message = message });

        return null;
    }
}
