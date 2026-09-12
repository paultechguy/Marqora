// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;

namespace PaulTechGuy.MQ.Docx;

/// <summary>One run of a highlighted code block: its text, and the token class it wears.</summary>
internal readonly record struct CodeToken(string Text, string? TokenClass);

/// <summary>
/// The three things in a document that only exist after a browser has drawn them, lifted back
/// out of the preview's markup.
///
/// A mermaid definition is a diagram only once mermaid has laid it out; an equation is typeset
/// only once KaTeX has run; code is colored only once highlight.js has been over it. None of
/// that can be done from a library with no browser in it, and all three are already sitting in
/// the string the preview hands back for the HTML export - so this reads them from there
/// rather than adding a second conversation with the shell.
///
/// Everything is keyed on the source line Markdig stamped on each block, not on the order
/// things appear in. Order looks simpler and is wrong: a document with one display equation
/// and one code block has two code-shaped things in the tree and one in the markup, because
/// display math renders as a div; the counters part company and every code block after that
/// point gets somebody else's colors. The line is exact, and it costs nothing.
///
/// Every lookup can fail, and failing is ordinary. A document exported before the preview
/// caught up, or with the preview never asked at all, simply gets no colors and no diagrams -
/// which is the property that makes a Word export degrade instead of refusing.
/// </summary>
internal sealed class PreviewHarvest
{
    private static readonly PreviewHarvest Nothing = new();

    private readonly Dictionary<int, string> _diagrams = [];
    private readonly Dictionary<int, IReadOnlyList<CodeToken>> _code = [];
    private readonly Dictionary<(int Line, int Ordinal), string> _math = [];

    private PreviewHarvest()
    {
    }

    /// <summary>Whether the preview gave us anything at all.</summary>
    public bool IsEmpty => _diagrams.Count == 0 && _code.Count == 0 && _math.Count == 0;

    public static PreviewHarvest From(string? renderedHtml)
    {
        if (string.IsNullOrWhiteSpace(renderedHtml))
        {
            return Nothing;
        }

        var harvest = new PreviewHarvest();

        harvest.ReadDiagrams(renderedHtml);
        harvest.ReadCode(renderedHtml);
        harvest.ReadMath(renderedHtml);

        return harvest;
    }

    /// <summary>The hash the shell knows a diagram by, so its picture can be asked for.</summary>
    public bool TryDiagram(int line, out string hash) => _diagrams.TryGetValue(line, out hash!);

    /// <summary>
    /// The colored runs of one code block.
    ///
    /// The caller compares the text against what the tree says before trusting it: the editor
    /// runs ahead of the preview by a debounce interval, so a block edited a moment ago comes
    /// back with the right shape and the wrong content.
    /// </summary>
    public bool TryCode(int line, out IReadOnlyList<CodeToken> tokens) =>
        _code.TryGetValue(line, out tokens!);

    /// <summary>
    /// The MathML KaTeX produced for one equation. The ordinal separates several inline
    /// equations in a single paragraph, which all share its line.
    /// </summary>
    public bool TryMath(int line, int ordinal, out string mathml) =>
        _math.TryGetValue((line, ordinal), out mathml!);

    /// <summary>
    /// Mermaid blocks, which the shell stamps with a hash of their definition once it has
    /// drawn them. A block that failed to parse carries no hash and is skipped, which is
    /// right: there is no picture to ask for.
    /// </summary>
    private void ReadDiagrams(string html)
    {
        int at = 0;

        while ((at = html.IndexOf("<pre", at, StringComparison.Ordinal)) >= 0)
        {
            int close = html.IndexOf('>', at);

            if (close < 0)
            {
                return;
            }

            ReadOnlySpan<char> tag = html.AsSpan(at, close - at);

            if (tag.Contains("mermaid", StringComparison.Ordinal)
                && Attribute(tag, "data-src-line") is { } line
                && int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                && Attribute(tag, "data-mq-diagram") is { Length: > 0 } hash)
            {
                _diagrams[number] = hash;
            }

            at = close + 1;
        }
    }

    /// <summary>
    /// Code blocks. The line is stamped on the inner element rather than the outer one, which
    /// is worth knowing and not worth guessing at: Markdig writes a fence as
    /// <c>&lt;pre&gt;&lt;code class="language-js" data-src-line="2"&gt;</c>.
    ///
    /// Every one is read, highlighted or not. The shell skips a language it does not know,
    /// leaving the block uncolored and without the marker class, and filtering on that marker
    /// would quietly lose those blocks rather than render them plain.
    /// </summary>
    private void ReadCode(string html)
    {
        int at = 0;

        while ((at = html.IndexOf("<code", at, StringComparison.Ordinal)) >= 0)
        {
            int close = html.IndexOf('>', at);

            if (close < 0)
            {
                return;
            }

            ReadOnlySpan<char> tag = html.AsSpan(at, close - at);

            // Code cannot nest, so the next closing tag is this element's.
            int end = html.IndexOf("</code>", close, StringComparison.Ordinal);

            if (end < 0)
            {
                return;
            }

            if (Attribute(tag, "data-src-line") is { } line
                && int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                _code[number] = Tokenize(html.AsSpan(close + 1, end - close - 1));
            }

            at = end + "</code>".Length;
        }
    }

    /// <summary>
    /// Equations, attributed to the last line stamp seen before them.
    ///
    /// Display math is a div carrying the stamp and holding one equation; inline math sits in
    /// a paragraph carrying the stamp and may hold several, which is what the ordinal counts.
    /// Walking the markup once and remembering the most recent stamp handles both without
    /// having to work out where any element ends.
    /// </summary>
    private void ReadMath(string html)
    {
        int at = 0;
        int line = -1;
        int ordinal = 0;

        while (at < html.Length)
        {
            int stamp = html.IndexOf("data-src-line=\"", at, StringComparison.Ordinal);
            int math = html.IndexOf("<math", at, StringComparison.Ordinal);

            if (math < 0)
            {
                return;
            }

            if (stamp >= 0 && stamp < math)
            {
                int from = stamp + "data-src-line=\"".Length;
                int to = html.IndexOf('"', from);

                if (to > from
                    && int.TryParse(
                        html.AsSpan(from, to - from),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int number)
                    && number != line)
                {
                    line = number;
                    ordinal = 0;
                }

                at = to + 1;
                continue;
            }

            int end = html.IndexOf("</math>", math, StringComparison.Ordinal);

            if (end < 0)
            {
                return;
            }

            end += "</math>".Length;

            if (line >= 0)
            {
                _math[(line, ordinal)] = html[math..end];
                ordinal++;
            }

            at = end;
        }
    }

    /// <summary>
    /// Splits highlighted markup into runs.
    ///
    /// The spans nest - a substitution inside a string, for instance - and the innermost class
    /// is the one that decides a run's color, so the classes are kept on a stack and the top
    /// of it wins. Anything outside a span is ordinary code with no class at all.
    /// </summary>
    private static List<CodeToken> Tokenize(ReadOnlySpan<char> markup)
    {
        var tokens = new List<CodeToken>();
        var classes = new Stack<string?>();
        var text = new System.Text.StringBuilder();

        void Flush(string? tokenClass)
        {
            if (text.Length == 0)
            {
                return;
            }

            tokens.Add(new CodeToken(WebUtility.HtmlDecode(text.ToString()), tokenClass));
            text.Clear();
        }

        for (int i = 0; i < markup.Length; i++)
        {
            if (markup[i] != '<')
            {
                text.Append(markup[i]);
                continue;
            }

            int close = markup[i..].IndexOf('>');

            if (close < 0)
            {
                break;
            }

            ReadOnlySpan<char> tag = markup.Slice(i + 1, close - 1);

            Flush(classes.Count > 0 ? classes.Peek() : null);

            if (tag.StartsWith("/span", StringComparison.OrdinalIgnoreCase))
            {
                if (classes.Count > 0)
                {
                    classes.Pop();
                }
            }
            else if (tag.StartsWith("span", StringComparison.OrdinalIgnoreCase))
            {
                classes.Push(Attribute(tag, "class"));
            }

            i += close;
        }

        Flush(classes.Count > 0 ? classes.Peek() : null);

        return tokens;
    }

    /// <summary>
    /// One attribute out of a tag's text, or null when it is not there. Double quotes only,
    /// which is what every one of these writers emits.
    /// </summary>
    private static string? Attribute(ReadOnlySpan<char> tag, string name)
    {
        int at = tag.IndexOf($"{name}=\"", StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        ReadOnlySpan<char> rest = tag[(at + name.Length + 2)..];
        int end = rest.IndexOf('"');

        return end < 0 ? null : rest[..end].ToString();
    }
}
