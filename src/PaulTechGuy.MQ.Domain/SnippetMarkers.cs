// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The two placeholders a snippet body may carry, named once.
///
/// A snippet is plain markdown, not a template language, and these are the whole of the
/// exception — deliberately, because the snippets folder is a folder of files a person edits
/// in whatever editor they like and a file full of sigils stops reading as markdown. Both are
/// removed on the way in, and both double their first character to mean themselves.
///
/// Here rather than in the editing assembly because a body is a domain concept — the
/// catalogue reads one from a file, the editor expands it, and the view model asks how much
/// of the document an expansion needs to see. Three callers, one statement of what the
/// sigils are.
/// </summary>
public static class SnippetMarkers
{
    /// <summary>Where the caret lands. Without one it lands after what was inserted.</summary>
    public const string Caret = "$0";

    /// <summary>A literal <c>$0</c>. Shell scripts and regular expressions carry one often enough.</summary>
    public const string EscapedCaret = "$$0";

    /// <summary>
    /// Where the text the snippet takes with it goes: the selection, or the paragraph the caret
    /// is in when there is no selection.
    ///
    /// Opt-in rather than automatic, and that is the point of it being a second marker rather
    /// than <see cref="Caret"/> doing double duty. Front Matter would drop a captured paragraph
    /// into its <c>title:</c> line and the Mermaid starters would drop one inside a fence, where
    /// prose is a syntax error. A snippet that wants the text says so.
    /// </summary>
    public const string Selection = "$SEL";

    /// <summary>A literal <c>$SEL</c>.</summary>
    public const string EscapedSelection = "$$SEL";

    /// <summary>
    /// Whether <paramref name="body"/> carries a live <see cref="Selection"/> marker, as opposed
    /// to an escaped one.
    ///
    /// Asked before the body is expanded, because a snippet that captures has to be handed the
    /// whole document rather than the few lines around the caret: a paragraph reaches as far as
    /// the blank lines either side of it, and neither is at a known distance.
    /// </summary>
    public static bool HasSelection(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return false;
        }

        for (int i = 0; i < body.Length; i++)
        {
            if (body[i] != '$')
            {
                continue;
            }

            ReadOnlySpan<char> rest = body.AsSpan(i);

            // The escape is checked first: "$$SEL" opens with "$" and would otherwise be read
            // as a literal dollar followed by a marker.
            if (rest.StartsWith(EscapedSelection, StringComparison.Ordinal))
            {
                i += EscapedSelection.Length - 1;

                continue;
            }

            if (rest.StartsWith(Selection, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
