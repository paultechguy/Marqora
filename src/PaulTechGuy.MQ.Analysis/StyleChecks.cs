// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Markdown;

namespace PaulTechGuy.MQ.Analysis;

/// <summary>
/// The style rules that can be reported where they happen.
///
/// Only the formatter's single-line rules are mirrored here: they match on one line, never
/// change how many lines there are, and their match position is the position to report. The
/// structural rules — renumbering, blank lines around blocks, table alignment, reflow — move
/// lines about as they run, so their original positions stop meaning anything partway
/// through and they are deliberately left out.
///
/// Everything found here is a Hint. The formatter fixes all of it on request, so squiggling
/// it in red would be shouting about something already solved.
/// </summary>
internal static partial class StyleChecks
{
    public static void Run(IReadOnlyList<string> lines, bool[] isProtected, List<Diagnostic> into)
    {
        for (int line = 0; line < lines.Count; line++)
        {
            if (isProtected[line])
            {
                continue;
            }

            string text = lines[line];

            // Inline rules run against a copy with code spans blanked out, so a backticked
            // example of bad syntax is not reported as bad syntax.
            string outsideCode = LineMasker.MaskCodeSpans(text);

            Check(HeadingWithoutSpace(), text, line, "heading-space",
                "Put a space between the hashes and the heading text.", into);

            CheckListMarker(text, line, into);

            Check(BlockquoteWithoutSpace(), text, line, "blockquote-space",
                "Put a space after the blockquote marker.", into);

            Check(TrailingWhitespace(), text, line, "trailing-whitespace",
                "Trailing whitespace.", into);

            Check(SpacedLinkSyntax(), outsideCode, line, "link-syntax",
                "Remove the space between the link text and its target.", into);
        }
    }

    private static void Check(
        Regex pattern,
        string text,
        int line,
        string rule,
        string message,
        List<Diagnostic> into)
    {
        Match match = pattern.Match(text);

        if (!match.Success)
        {
            return;
        }

        // Named group "at" marks the part worth underlining when the whole match is wider
        // than the problem.
        Report(match.Groups["at"].Success ? match.Groups["at"] : match, line, rule, message, into);
    }

    /// <summary>
    /// The bullet rule, which needs a second look the others do not.
    ///
    /// A line opening with an asterisk is ambiguous: "*emphasis*" and a bullet whose space
    /// went missing are written identically. An asterisk with a partner later on the line is
    /// read as emphasis and left alone, which is both the reading a renderer gives it and
    /// what the formatter already does, so squiggling it would be pointing at something the
    /// formatter would refuse to change.
    /// </summary>
    private static void CheckListMarker(string text, int line, List<Diagnostic> into)
    {
        Match match = ListMarkerWithoutSpace().Match(text);

        if (!match.Success)
        {
            return;
        }

        Group marker = match.Groups["at"];

        if (text[marker.Index] == '*' && text.IndexOf('*', marker.Index + 1) >= 0)
        {
            return;
        }

        Report(marker, line, "list-marker-space", "Put a space after the list marker.", into);
    }

    private static void Report(Group span, int line, string rule, string message, List<Diagnostic> into) =>
        into.Add(new Diagnostic
        {
            Line = line,
            Column = span.Index,
            EndColumn = span.Index + Math.Max(1, span.Length),
            Severity = DiagnosticSeverity.Hint,
            Rule = rule,
            Message = message,
        });

    [GeneratedRegex(@"^\s{0,3}(?<at>#{1,6})[^\s#]")]
    private static partial Regex HeadingWithoutSpace();

    [GeneratedRegex(@"^\s*(?<at>[-*+])[^\s\-*+]")]
    private static partial Regex ListMarkerWithoutSpace();

    [GeneratedRegex(@"^\s*(?<at>>)[^\s>]")]
    private static partial Regex BlockquoteWithoutSpace();

    [GeneratedRegex(@"(?<at>[ \t]+)$")]
    private static partial Regex TrailingWhitespace();

    [GeneratedRegex(@"\](?<at>\s+)\(")]
    private static partial Regex SpacedLinkSyntax();
}
