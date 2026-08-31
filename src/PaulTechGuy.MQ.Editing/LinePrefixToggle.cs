// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Editing;

/// <summary>The line-leading constructs that can be switched on and off.</summary>
internal enum LinePrefixKind
{
    Blockquote,
    Bullet,
    Numbered,
    Task,
    Heading,
}

/// <summary>
/// Quotes, lists and headings: markers that live at the front of a line and apply to every
/// line the selection touches.
/// </summary>
internal static partial class LinePrefixToggle
{
    public static EditResult Apply(EditContext context, LinePrefixKind kind, int headingLevel = 1)
    {
        List<(int Line, string Text)> content = Targets(context, out TextRange selection);
        if (content.Count == 0)
        {
            return EditResult.None;
        }

        // Off only when every line already has it. A part-marked selection gets finished
        // rather than cleared, which is what people expect from a toolbar toggle.
        bool remove = content.TrueForAll(t => HasPrefix(t.Text, kind, headingLevel));

        int number = 1;

        return Rewrite(content, selection, (_, text) => remove
            ? Remove(text, kind)
            : Add(text, kind, headingLevel, number++));
    }

    /// <summary>
    /// Whether pressing this prefix's button would clear it rather than apply it, which is
    /// the same all-or-nothing question <see cref="Apply"/> asks before deciding.
    /// </summary>
    public static bool WouldRemove(EditContext context, LinePrefixKind kind, int headingLevel = 1)
    {
        List<(int Line, string Text)> content = Targets(context, out _);

        return content.Count > 0 && content.TrueForAll(t => HasPrefix(t.Text, kind, headingLevel));
    }

    /// <summary>
    /// The heading level shared by every line the selection touches, or zero when they
    /// disagree or none is a heading.
    ///
    /// Zero for a mixed selection rather than the first line's level, because
    /// <see cref="Apply"/> treats headings all-or-nothing too — so the label and the action
    /// agree about what the selection is.
    /// </summary>
    public static int HeadingLevelOf(EditContext context)
    {
        List<(int Line, string Text)> content = Targets(context, out _);

        if (content.Count == 0)
        {
            return 0;
        }

        int level = LevelOf(content[0].Text);

        return level > 0 && content.TrueForAll(t => LevelOf(t.Text) == level) ? level : 0;
    }

    private static int LevelOf(string text) =>
        Heading().Match(text) is { Success: true } heading ? heading.Groups["hashes"].Value.Length : 0;

    /// <summary>Moves headings a level up or down, leaving plain lines alone.</summary>
    public static EditResult Shift(EditContext context, int direction)
    {
        List<(int Line, string Text)> content = Targets(context, out TextRange selection);
        if (content.Count == 0)
        {
            return EditResult.None;
        }

        return Rewrite(content, selection, (_, text) =>
        {
            Match heading = Heading().Match(text);
            int current = heading.Success ? heading.Groups["hashes"].Value.Length : 0;

            // Nothing to take away from a line that is not a heading. Adding one on the
            // way down would be the opposite of what was asked for.
            if (current == 0 && direction < 0)
            {
                return text;
            }

            (string indent, string body) = StripMarkers(text);

            return indent + new string('#', Math.Clamp(current + direction, 1, 6)) + " " + body;
        });
    }

    /// <summary>
    /// The lines a command should act on: those the selection touches, minus the blank
    /// ones, which separate blocks rather than belonging to them. A selection that is
    /// entirely blank falls back to the caret's own line, so the command still does
    /// something in an empty document.
    /// </summary>
    private static List<(int Line, string Text)> Targets(EditContext context, out TextRange selection)
    {
        selection = Selections.Normalize(context);

        List<(int Line, string Text)> targets = [];
        for (int i = selection.Start.Line; i <= selection.End.Line; i++)
        {
            if (context.LineAt(i) is { } text)
            {
                targets.Add((i, text));
            }
        }

        if (targets.Count == 0)
        {
            return targets;
        }

        List<(int Line, string Text)> content = targets.FindAll(t => t.Text.Trim().Length > 0);

        return content.Count > 0 ? content : [targets[0]];
    }

    private static EditResult Rewrite(
        List<(int Line, string Text)> content,
        TextRange selection,
        Func<int, string, string> transform)
    {
        List<TextEdit> edits = [];
        int startDelta = 0;
        int endDelta = 0;

        foreach ((int line, string text) in content)
        {
            string replacement = transform(line, text);
            if (replacement == text)
            {
                continue;
            }

            edits.Add(new TextEdit(Selections.WholeLine(line, text), replacement));

            int delta = replacement.Length - text.Length;
            if (line == selection.Start.Line)
            {
                startDelta = delta;
            }

            if (line == selection.End.Line)
            {
                endDelta = delta;
            }
        }

        if (edits.Count == 0)
        {
            return EditResult.None;
        }

        // Carry the selection along by however much its own lines grew or shrank, so it
        // still covers the same words and the command can be pressed twice running.
        var moved = new TextRange(
            new TextPosition(selection.Start.Line, Math.Max(0, selection.Start.Column + startDelta)),
            new TextPosition(selection.End.Line, Math.Max(0, selection.End.Column + endDelta)));

        return new EditResult(edits, moved);
    }

    private static bool HasPrefix(string text, LinePrefixKind kind, int headingLevel)
    {
        Match bullet = BulletMarker().Match(text);

        return kind switch
        {
            LinePrefixKind.Bullet => bullet.Success && !bullet.Groups["task"].Success,
            LinePrefixKind.Task => bullet.Success && bullet.Groups["task"].Success,
            LinePrefixKind.Numbered => OrderedMarker().IsMatch(text),
            LinePrefixKind.Blockquote => Blockquote().IsMatch(text),
            LinePrefixKind.Heading => Heading().Match(text) is { Success: true } h
                && h.Groups["hashes"].Value.Length == headingLevel,
            _ => false,
        };
    }

    private static string Remove(string text, LinePrefixKind kind)
    {
        if (kind == LinePrefixKind.Blockquote)
        {
            Match quote = Blockquote().Match(text);

            return quote.Success ? quote.Groups["indent"].Value + text[quote.Length..] : text;
        }

        (string indent, string body) = StripMarkers(text);

        return indent + body;
    }

    private static string Add(string text, LinePrefixKind kind, int headingLevel, int number)
    {
        // Quoting nests, so a line that is already quoted gains another level rather than
        // having the first one rewritten.
        if (kind == LinePrefixKind.Blockquote)
        {
            int indentLength = text.Length - text.TrimStart(' ', '\t').Length;

            return text[..indentLength] + "> " + text[indentLength..];
        }

        // Everything else replaces whatever marker the line already had, so switching a
        // numbered list to bullets, or an H3 to an H1, is one press rather than two.
        (string indent, string body) = StripMarkers(text);

        string marker = kind switch
        {
            LinePrefixKind.Bullet => "- ",
            LinePrefixKind.Task => "- [ ] ",
            LinePrefixKind.Numbered => $"{number}. ",
            LinePrefixKind.Heading => new string('#', headingLevel) + " ",
            _ => string.Empty,
        };

        return indent + marker + body;
    }

    /// <summary>
    /// Splits a line into its indentation and whatever follows any list marker or heading
    /// hashes it carries.
    /// </summary>
    private static (string Indent, string Body) StripMarkers(string text)
    {
        if (BulletMarker().Match(text) is { Success: true } bullet)
        {
            return (bullet.Groups["indent"].Value, text[bullet.Length..]);
        }

        if (OrderedMarker().Match(text) is { Success: true } ordered)
        {
            return (ordered.Groups["indent"].Value, text[ordered.Length..]);
        }

        if (Heading().Match(text) is { Success: true } heading)
        {
            return (heading.Groups["indent"].Value, text[heading.Length..]);
        }

        int indentLength = text.Length - text.TrimStart(' ', '\t').Length;

        return (text[..indentLength], text[indentLength..]);
    }

    [GeneratedRegex(@"^(?<indent>[ \t]*)[-*+][ \t]+(?<task>\[[ xX]\][ \t]+)?")]
    private static partial Regex BulletMarker();

    [GeneratedRegex(@"^(?<indent>[ \t]*)\d+[.)][ \t]+")]
    private static partial Regex OrderedMarker();

    [GeneratedRegex(@"^(?<indent>[ \t]*)>[ \t]?")]
    private static partial Regex Blockquote();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<hashes>#{1,6})[ \t]+")]
    private static partial Regex Heading();
}
