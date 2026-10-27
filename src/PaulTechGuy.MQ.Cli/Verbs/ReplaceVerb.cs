// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Text;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Finding;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// replace: Replace All (CLI-VB-RPL-01), with the Find bar's three options and through the app's
/// own replacer, so code, matches and $-patterns are treated exactly as the app treats them.
///
/// A regular expression is checked before any file is read: one that will not compile ends the run
/// with exit 2 and the parser's own words. Each file is its own Replace All, so the app's ceiling of
/// matches applies per file; a file that reaches it is said so rather than being quietly half done.
/// </summary>
internal static class ReplaceVerb
{
    public sealed record Options(Option<string> Find, Option<string> With, Option<bool> MatchCase, Option<bool> WholeWord, Option<bool> Regex);

    public static Task<ExitCode> RunAsync(RunContext context, ParseResult parse, InputOptions inputs, ModeOptions modes, Options options)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(options);

        string term = parse.GetValue(options.Find) ?? string.Empty;

        if (term.Length == 0)
        {
            throw new UsageException("--find needs some text to find.");
        }

        var query = new ReplaceQuery
        {
            Find = new FindQuery
            {
                Term = term,
                MatchCase = parse.GetValue(options.MatchCase),
                WholeWord = parse.GetValue(options.WholeWord),
                UseRegex = parse.GetValue(options.Regex),
            },
            Replacement = parse.GetValue(options.With) ?? string.Empty,
        };

        // The replacer compiles the pattern before it looks at a document, so asking it about none
        // is how a bad pattern is found before any file is.
        if (query.Find.UseRegex && DocumentReplacer.Replace(query, []).Error is { } invalid)
        {
            throw new UsageException($"--find is not a valid regular expression: {invalid}");
        }

        return TextVerbRunner.RunAsync(context, parse, inputs, modes, document =>
        {
            ReplaceResults results = DocumentReplacer.Replace(
                query,
                [new FindDocument(Guid.NewGuid(), document.Display, document.LocationPath, document.File.Text)],
                context.CancellationToken);

            if (results.Error is { } error)
            {
                return VerbResult.Failed(document.File.Text, error);
            }

            string text = results.Documents.Count > 0 ? results.Documents[0].NewText : document.File.Text;
            DocumentFinding[] findings = results.Truncated
                ? [new DocumentFinding(0, 0, "replace-limit", FindingSeverity.Warning, $"Stopped at {DocumentFinder.MatchLimit} matches, as Replace All does; run again to replace the rest.")]
                : [];

            return new VerbResult(text, findings, results.TotalMatches == 1 ? "1 replaced" : $"{results.TotalMatches} replaced");
        });
    }
}
