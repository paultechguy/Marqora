// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>Which way a mermaid diagram is drawn. See <see cref="DiagramLayout"/>.</summary>
public enum DiagramDirection
{
    /// <summary>
    /// Neither of the two below: bottom to top or right to left, which nothing offers but
    /// nothing changes either. Also what asks for no change at all.
    /// </summary>
    AsIs,

    TopToBottom,
    LeftToRight,
}

/// <summary>
/// One change to a diagram's direction, in zero-based lines and columns.
///
/// Either a span of one line replaced by <see cref="Text"/>, or - when the diagram has no
/// direction written anywhere and its type wants one on a line of its own - a whole new line,
/// <see cref="Text"/>, to go in after <see cref="Line"/>. The caller turns the new line into an
/// insertion at the end of <see cref="Line"/>, in the document's own line ending, which this
/// cannot know.
/// </summary>
public sealed record DiagramLayoutEdit(int Line, int Start, int End, string Text, bool InsertsLine);

/// <summary>
/// Which way a mermaid diagram is drawn, read from its definition and rewritten there.
///
/// Reflowing a wide diagram is mermaid's own job: change the direction it is told and it lays
/// the whole thing out again, text upright, and every output follows without knowing anything
/// happened. So this edits the definition rather than turning the picture, and only the
/// diagram's own direction - a subgraph or composite state that says its own is left saying
/// it. Mermaid ignores those anyway whenever a link crosses the boundary, so rewriting them
/// would mostly change text without changing the drawing.
///
/// Each diagram type spells its direction in one of three places, and a type not listed has
/// none: a sequence diagram's width is its participants and a Gantt chart's is its dates, and
/// no keyword changes either. Those answer null, which is what grays the menu item rather than
/// offering a change that would do nothing.
///
/// The source pane's right-click and the Format menu both come here, so the two cannot
/// disagree about which diagrams can be reflowed or how. There is deliberately no formatter
/// rule doing this to every diagram at once: a document's diagrams are each drawn the way
/// that suits them, and one is turned when someone chooses to turn it.
/// </summary>
public static class DiagramLayout
{
    /// <summary>Where a type writes its direction.</summary>
    private enum Site
    {
        /// <summary>After the type on its first line: <c>flowchart LR</c>, <c>timeline TD</c>.</summary>
        Header,

        /// <summary>A line of its own anywhere in the body: <c>direction LR</c>.</summary>
        Statement,

        /// <summary>After the type, with a colon: <c>gitGraph TB:</c>.</summary>
        GitGraph,
    }

    /// <summary>
    /// How one type is reflowed: where its direction goes, which way it runs when nothing is
    /// written, and the word it takes for top to bottom - TB everywhere except a timeline, whose
    /// grammar knows only LR and TD.
    /// </summary>
    private sealed record Kind(Site Site, DiagramDirection Default, string Down);

    private static readonly Kind Flow = new(Site.Header, DiagramDirection.TopToBottom, "TB");
    private static readonly Kind Body = new(Site.Statement, DiagramDirection.TopToBottom, "TB");

    /// <summary>
    /// The types that have a direction, by the word mermaid recognizes them by. Ordinal, as
    /// mermaid's own detectors are: "Flowchart" is not a diagram it will draw.
    /// </summary>
    private static readonly Dictionary<string, Kind> Kinds = new(StringComparer.Ordinal)
    {
        ["flowchart"] = Flow,
        ["graph"] = Flow,
        ["flowchart-elk"] = Flow,
        ["stateDiagram"] = Body,
        ["stateDiagram-v2"] = Body,
        ["classDiagram"] = Body,
        ["classDiagram-v2"] = Body,
        ["erDiagram"] = Body,
        ["requirementDiagram"] = Body,
        ["requirement"] = Body,
        ["gitGraph"] = new(Site.GitGraph, DiagramDirection.LeftToRight, "TB"),
        ["timeline"] = new(Site.Header, DiagramDirection.LeftToRight, "TD"),
    };

    /// <summary>
    /// Where a diagram's direction was found: the span holding it, or - when nothing is written -
    /// where one would go, with <see cref="Token"/> empty.
    /// </summary>
    private sealed record Found(Kind Kind, int Header, int Line, int Start, int End, string Token);

    /// <summary>
    /// Every mermaid fence in the document, as its opening and closing lines. A fence left open
    /// runs to the end of the document, as the preview draws it, and closes on
    /// <c>lines.Count</c>.
    /// </summary>
    public static IReadOnlyList<(int Open, int Close)> Fences(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var fences = new List<(int Open, int Close)>();

        string? marker = null;
        bool mermaid = false;
        int open = -1;

        for (int i = 0; i < lines.Count; i++)
        {
            string trimmed = lines[i].Trim();

            if (marker is null)
            {
                if (OpensFence(trimmed, out marker, out string info))
                {
                    open = i;
                    mermaid = string.Equals(FirstWord(info), "mermaid", StringComparison.OrdinalIgnoreCase);
                }

                continue;
            }

            // A fence closes on a run of the same character at least as long as the opener,
            // with nothing after it.
            if (trimmed.Length >= marker.Length && trimmed.All(c => c == marker[0]))
            {
                if (mermaid)
                {
                    fences.Add((open, i));
                }

                marker = null;
            }
        }

        if (marker is not null && mermaid)
        {
            fences.Add((open, lines.Count));
        }

        return fences;
    }

    /// <summary>The mermaid fence holding <paramref name="line"/>, its own fence lines included.</summary>
    public static (int Open, int Close)? FenceAt(IReadOnlyList<string> lines, int line)
    {
        foreach ((int open, int close) in Fences(lines))
        {
            if (line >= open && line <= close)
            {
                return (open, close);
            }
        }

        return null;
    }

    /// <summary>
    /// Which way the diagram in the fence is drawn: what is written, or its type's default when
    /// nothing is. <see cref="DiagramDirection.AsIs"/> stands for bottom to top and right to
    /// left, which this offers no way to choose but leaves alone. Null when the diagram's type
    /// has no direction to change.
    /// </summary>
    public static DiagramDirection? Current(IReadOnlyList<string> lines, int open, int close)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return Locate(lines, open, close) is { } found ? Read(found) : null;
    }

    /// <summary>
    /// The one change that makes the diagram in the fence run <paramref name="want"/>, or null
    /// when it already does, when <paramref name="want"/> is <see cref="DiagramDirection.AsIs"/>,
    /// or when its type has no direction to change.
    /// </summary>
    public static DiagramLayoutEdit? EditFor(
        IReadOnlyList<string> lines, int open, int close, DiagramDirection want)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (want is not (DiagramDirection.TopToBottom or DiagramDirection.LeftToRight)
            || Locate(lines, open, close) is not { } found
            || Read(found) == want)
        {
            return null;
        }

        string token = want == DiagramDirection.TopToBottom ? found.Kind.Down : "LR";

        switch (found.Kind.Site)
        {
            case Site.GitGraph:
                // The whole tail is rewritten rather than the word alone, so a header with no
                // direction gains its colon along with it: "gitGraph" becomes "gitGraph TB:".
                return new DiagramLayoutEdit(found.Line, found.Start, found.End, $" {token}:", false);

            case Site.Statement when found.Token.Length == 0:
                return new DiagramLayoutEdit(
                    found.Header,
                    0,
                    0,
                    $"{BodyIndent(lines, found.Header, close)}direction {token}",
                    true);

            case Site.Header when found.Token.Length == 0:
                return new DiagramLayoutEdit(found.Line, found.Start, found.Start, $" {token}", false);

            default:
                return new DiagramLayoutEdit(found.Line, found.Start, found.End, token, false);
        }
    }

    /// <summary>Finds where the diagram's direction is written, or where it would be.</summary>
    private static Found? Locate(IReadOnlyList<string> lines, int open, int close)
    {
        if (DiagramType.HeaderOf(lines, open) is not { } header
            || header.Line >= close
            || !Kinds.TryGetValue(header.Type, out Kind? kind))
        {
            return null;
        }

        string text = lines[header.Line];
        int keywordEnd = (text.Length - text.TrimStart().Length) + header.Type.Length;

        return kind.Site switch
        {
            Site.Header => InHeader(kind, header.Line, text, keywordEnd),
            Site.GitGraph => InGitGraph(kind, header.Line, text, keywordEnd),
            _ => InBody(kind, lines, header.Line, close),
        };
    }

    /// <summary>The word after the type, if it is a direction; otherwise the place one goes.</summary>
    private static Found InHeader(Kind kind, int line, string text, int keywordEnd)
    {
        int start = SkipBlanks(text, keywordEnd);
        int end = start;

        while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != ';')
        {
            end++;
        }

        string word = text[start..end];

        return IsDirection(word)
            ? new Found(kind, line, line, start, end, word)
            : new Found(kind, line, line, keywordEnd, keywordEnd, string.Empty);
    }

    /// <summary>Everything after <c>gitGraph</c> up to and including its colon.</summary>
    private static Found InGitGraph(Kind kind, int line, string text, int keywordEnd)
    {
        int at = SkipBlanks(text, keywordEnd);
        string word = string.Empty;

        foreach (string candidate in (string[])["LR", "TB", "BT"])
        {
            if (string.CompareOrdinal(text, at, candidate, 0, candidate.Length) == 0)
            {
                word = candidate;
                at = SkipBlanks(text, at + candidate.Length);
                break;
            }
        }

        if (at < text.Length && text[at] == ':')
        {
            at++;
        }

        return new Found(kind, line, line, keywordEnd, at, word);
    }

    /// <summary>
    /// A <c>direction</c> statement at the top level of the body - not inside a composite
    /// state, a namespace, or anything else in braces, whose own direction is its own.
    /// </summary>
    private static Found InBody(Kind kind, IReadOnlyList<string> lines, int header, int close)
    {
        int depth = 0;

        for (int i = header + 1; i < close && i < lines.Count; i++)
        {
            string text = lines[i];
            string trimmed = text.Trim();

            if (trimmed.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            if (depth == 0 && trimmed.StartsWith("direction", StringComparison.Ordinal))
            {
                int keywordEnd = text.IndexOf("direction", StringComparison.Ordinal) + "direction".Length;
                int start = SkipBlanks(text, keywordEnd);
                int end = start;

                while (end < text.Length && char.IsLetter(text[end]))
                {
                    end++;
                }

                if (start > keywordEnd && IsDirection(text[start..end]))
                {
                    return new Found(kind, header, i, start, end, text[start..end]);
                }
            }

            depth = Math.Max(0, depth + trimmed.Count(c => c == '{') - trimmed.Count(c => c == '}'));
        }

        return new Found(kind, header, header, 0, 0, string.Empty);
    }

    private static DiagramDirection Read(Found found) =>
        found.Token.Length == 0
            ? found.Kind.Default
            : found.Token.ToUpperInvariant() switch
            {
                "TB" or "TD" or "V" => DiagramDirection.TopToBottom,
                "LR" or ">" => DiagramDirection.LeftToRight,
                _ => DiagramDirection.AsIs,
            };

    /// <summary>
    /// Mermaid's directions, in either case: the four pairs of letters, TD for TB, and the
    /// arrows a flowchart also takes.
    /// </summary>
    private static bool IsDirection(string word) =>
        word.ToUpperInvariant() is "TB" or "TD" or "BT" or "LR" or "RL" or ">" or "<" or "^" or "V";

    /// <summary>
    /// The indentation a new body line takes: whatever the first line under the header uses, so
    /// it lines up with the rest, or four spaces past the header in an empty diagram.
    /// </summary>
    private static string BodyIndent(IReadOnlyList<string> lines, int header, int close)
    {
        for (int i = header + 1; i < close && i < lines.Count; i++)
        {
            string text = lines[i].TrimEnd();

            if (text.Length > 0)
            {
                return text[..(text.Length - text.TrimStart().Length)];
            }
        }

        string own = lines[header];

        return own[..(own.Length - own.TrimStart().Length)] + "    ";
    }

    private static int SkipBlanks(string text, int at)
    {
        while (at < text.Length && text[at] is ' ' or '\t')
        {
            at++;
        }

        return at;
    }

    /// <summary>
    /// Reads a run of three or more backticks or tildes opening a fence, and the info string
    /// after it. A backtick fence's info string may not contain a backtick.
    /// </summary>
    private static bool OpensFence(string trimmed, out string? marker, out string info)
    {
        marker = null;
        info = string.Empty;

        if (trimmed.Length < 3 || trimmed[0] is not ('`' or '~'))
        {
            return false;
        }

        char c = trimmed[0];
        int run = 0;

        while (run < trimmed.Length && trimmed[run] == c)
        {
            run++;
        }

        if (run < 3 || (c == '`' && trimmed[run..].Contains('`', StringComparison.Ordinal)))
        {
            return false;
        }

        marker = new string(c, run);
        info = trimmed[run..].Trim();

        return true;
    }

    private static string FirstWord(string info)
    {
        int end = 0;

        while (end < info.Length && !char.IsWhiteSpace(info[end]))
        {
            end++;
        }

        return info[..end];
    }
}
