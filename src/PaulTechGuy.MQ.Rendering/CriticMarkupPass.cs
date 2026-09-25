// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// A review comment's note, <c>{&gt;&gt;like this&lt;&lt;}</c>, gathered into one inline so it can
/// be drawn as a note rather than as the characters that wrote it.
/// </summary>
public sealed class CriticNoteInline : ContainerInline
{
}

/// <summary>Draws a <see cref="CriticNoteInline"/> as <c>&lt;span class="mq-critic-note"&gt;</c>.</summary>
public sealed class CriticNoteRenderer : HtmlObjectRenderer<CriticNoteInline>
{
    protected override void Write(HtmlRenderer renderer, CriticNoteInline obj)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(obj);

        if (renderer.EnableHtmlForInline)
        {
            renderer.Write("<span class=\"mq-critic-note\">");
            renderer.WriteChildren(obj);
            renderer.Write("</span>");
        }
        else
        {
            renderer.WriteChildren(obj);
        }
    }
}

/// <summary>
/// Reads the CriticMarkup a review's Copy as Markdown writes, so a reviewed document pasted back
/// into Marqora shows its comments as comments.
///
/// <code>{==the passage==}{&gt;&gt;the comment&lt;&lt;}</code>
///
/// Without this the preview reads <c>{==…==}</c> through the highlight extension: yellow, with a
/// stray brace at each end, and the yellow stopping at every bold or code word inside because
/// those draw their own backgrounds. The comment itself was the characters that wrote it.
///
/// A pass over the parsed document rather than a rewrite of the text before parsing, for the
/// reason every other pass here is one: the parse records where each link and block sits, the
/// analyzer and the scroll sync work from those positions, and text changed before the parse
/// would move them. Here nothing moves. A <c>==</c> highlight with a brace against each side is
/// the passage - it keeps its <c>mark</c> and is classed <c>mq-critic</c>, and the braces are
/// taken off the text beside it - and everything from <c>{&gt;&gt;</c> to <c>&lt;&lt;}</c> in the
/// same line of inlines is gathered into a <see cref="CriticNoteInline"/>, formatting and all.
/// Markup with no partner is left exactly as written.
/// </summary>
public static class CriticMarkupPass
{
    private const string NoteOpen = "{>>";
    private const string NoteClose = "<<}";

    public static void Apply(MarkdigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (LeafBlock leaf in document.Descendants<LeafBlock>())
        {
            if (leaf.Inline is { } root)
            {
                Rewrite(root);
            }
        }
    }

    private static void Rewrite(ContainerInline container)
    {
        List<Inline> original = [.. container];

        if (!original.Any(i => i is LiteralInline or EmphasisInline))
        {
            return;
        }

        var items = new List<Inline>(original);
        var notes = new List<(CriticNoteInline Note, List<Inline> Inside)>();

        bool changed = MarkPassages(items);
        List<Inline> result = GatherNotes(items, notes, ref changed);

        if (!changed)
        {
            return;
        }

        // Every original child comes off first, and only then is anything attached, so an inline
        // moving into a note is never taken back out of it by the clearing.
        foreach (Inline item in original)
        {
            item.Remove();
        }

        foreach ((CriticNoteInline note, List<Inline> inside) in notes)
        {
            foreach (Inline inner in inside)
            {
                note.AppendChild(inner);
            }
        }

        foreach (Inline item in result)
        {
            container.AppendChild(item);
        }
    }

    /// <summary>A <c>==</c> highlight with <c>{</c> against its start and <c>}</c> against its end is a commented passage.</summary>
    private static bool MarkPassages(List<Inline> items)
    {
        bool changed = false;

        for (int i = 1; i + 1 < items.Count; i++)
        {
            if (items[i] is not EmphasisInline { DelimiterChar: '=', DelimiterCount: 2 } passage
                || items[i - 1] is not LiteralInline before
                || items[i + 1] is not LiteralInline after)
            {
                continue;
            }

            string left = before.Content.ToString();
            string right = after.Content.ToString();

            if (!left.EndsWith('{') || !right.StartsWith('}'))
            {
                continue;
            }

            passage.GetAttributes().AddClass("mq-critic");
            items[i - 1] = Literal(left[..^1], before);
            items[i + 1] = Literal(right[1..], after);
            changed = true;
        }

        return changed;
    }

    /// <summary>Everything from <c>{&gt;&gt;</c> to the next <c>&lt;&lt;}</c>, gathered into one note.</summary>
    private static List<Inline> GatherNotes(
        List<Inline> items,
        List<(CriticNoteInline Note, List<Inline> Inside)> notes,
        ref bool changed)
    {
        var result = new List<Inline>(items.Count);
        int i = 0;

        while (i < items.Count)
        {
            if (items[i] is not LiteralInline literal
                || literal.Content.ToString() is not { } text
                || text.IndexOf(NoteOpen, StringComparison.Ordinal) is var open && open < 0)
            {
                result.Add(items[i]);
                i++;
                continue;
            }

            string before = text[..open];
            string rest = text[(open + NoteOpen.Length)..];

            var inside = new List<Inline>();
            string? tail = null;
            int last = i;

            int close = rest.IndexOf(NoteClose, StringComparison.Ordinal);

            if (close >= 0)
            {
                inside.Add(Literal(rest[..close], literal));
                tail = rest[(close + NoteClose.Length)..];
            }
            else
            {
                inside.Add(Literal(rest, literal));

                for (int j = i + 1; j < items.Count; j++)
                {
                    if (items[j] is LiteralInline next
                        && next.Content.ToString() is { } nextText
                        && nextText.IndexOf(NoteClose, StringComparison.Ordinal) is var end && end >= 0)
                    {
                        inside.Add(Literal(nextText[..end], next));
                        tail = nextText[(end + NoteClose.Length)..];
                        last = j;
                        break;
                    }

                    inside.Add(items[j]);
                }
            }

            // No partner: the characters stand as written, and the search moves past them.
            if (tail is null)
            {
                result.Add(items[i]);
                i++;
                continue;
            }

            if (before.Length > 0)
            {
                result.Add(Literal(before, literal));
            }

            var note = new CriticNoteInline();
            notes.Add((note, [.. inside.Where(n => n is not LiteralInline { Content.Length: 0 })]));

            result.Add(note);
            changed = true;

            // What follows the note on the same run of text may open another one, so it is looked
            // at again rather than passed straight through.
            if (tail.Length > 0)
            {
                items[last] = Literal(tail, (LiteralInline)items[last]);
                i = last;
            }
            else
            {
                i = last + 1;
            }
        }

        return result;
    }

    /// <summary>A literal holding <paramref name="text"/>, placed where <paramref name="from"/> was.</summary>
    private static LiteralInline Literal(string text, LiteralInline from) =>
        new(text) { Line = from.Line, Column = from.Column, Span = from.Span };
}
