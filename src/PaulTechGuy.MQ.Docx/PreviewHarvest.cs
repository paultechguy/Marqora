// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Abstractions.Ui;

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
internal sealed partial class PreviewHarvest
{
    private static readonly PreviewHarvest Nothing = new();

    private readonly Dictionary<int, string> _diagrams = [];
    private readonly Dictionary<int, IReadOnlyList<CodeToken>> _code = [];
    private readonly Dictionary<(int Line, int Ordinal), string> _math = [];
    private readonly Dictionary<int, HtmlBlockBox> _boxes = [];
    private readonly Dictionary<string, string> _roles = new(StringComparer.Ordinal);
    private readonly Dictionary<(int Line, int Ordinal), IReadOnlyList<string>> _numbers = [];
    private readonly Dictionary<(int Line, int Ordinal), MathPicture> _mathPictures = [];

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
        harvest.ReadBoxes(renderedHtml);

        return harvest;
    }

    /// <summary>The hash the shell knows a diagram by, so its picture can be asked for.</summary>
    public bool TryDiagram(int line, out string hash) => _diagrams.TryGetValue(line, out hash!);

    /// <summary>
    /// Whether Word draws this diagram's type correctly from its SVG, words and all, so it can
    /// go in as a vector with its PNG as the fallback.
    ///
    /// The ten types spike S6 saw Word 365 draw right (docs/Export-Alignment-Plan.md §4, §7.2).
    /// A type whose labels are HTML in a foreignObject - flowchart, class, state, ER, journey,
    /// block, kanban, mindmap - lost every word, and the timeline, treemap and git graph lost
    /// fills or text to mermaid's class-based styles; those keep the PNG. A type joins this
    /// list only after Word has been seen drawing it.
    /// </summary>
    public bool DrawsAsVector(string hash) =>
        _roles.TryGetValue(hash, out string? role) && VectorTypes.Contains(role);

    private static readonly HashSet<string> VectorTypes = new(StringComparer.Ordinal)
    {
        "sequence", "gantt", "pie", "quadrantChart", "c4",
        "sankey", "xychart", "packet", "architecture", "radar",
    };

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
    /// The numbers the preview gave an equation - one per line of an align, one for a numbered
    /// equation - as the reader sees them, "(1)". None for an equation that is not numbered.
    /// </summary>
    public IReadOnlyList<string> NumbersOf(int line, int ordinal) =>
        _numbers.TryGetValue((line, ordinal), out IReadOnlyList<string>? numbers) ? numbers : [];

    /// <summary>Every equation the preview gave, by its block's line and its ordinal there.</summary>
    public IEnumerable<KeyValuePair<(int Line, int Ordinal), string>> Equations => _math;

    /// <summary>
    /// A picture of an equation, drawn by the preview, for one the converter cannot write as a
    /// Word equation. Fetched before the walk, as the diagrams are; see DocxExporter.
    /// </summary>
    public void AddMathPicture(int line, int ordinal, MathPicture picture) => _mathPictures[(line, ordinal)] = picture;

    public bool TryMathPicture(int line, int ordinal, out MathPicture picture) =>
        _mathPictures.TryGetValue((line, ordinal), out picture!);

    [GeneratedRegex("data-mq-eqn=\"([^\"]*)\"")]
    private static partial Regex EquationNumber();

    /// <summary>
    /// The box the preview drew round a raw HTML block - its borders, fill and padding, as the
    /// shell measured them (htmlBlockBoxes in app.js) - keyed by the block's line.
    /// </summary>
    public bool TryBox(int line, out HtmlBlockBox box) => _boxes.TryGetValue(line, out box!);

    /// <summary>
    /// Each block's box, from the attribute the shell stamps on the element straight after the
    /// block's source-line marker. The marker carries the line; the element carries the box.
    /// </summary>
    private void ReadBoxes(string html)
    {
        foreach (Match match in BoxedBlock().Matches(html))
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int line)
                && HtmlBlockBox.Parse(WebUtility.HtmlDecode(match.Groups[2].Value)) is { } box)
            {
                _boxes[line] = box;
            }
        }
    }

    [GeneratedRegex("<div class=\"mq-src-marker\"[^>]*?data-src-line=\"(\\d+)\"[^>]*></div>\\s*<[a-zA-Z][^>]*?\\sdata-mq-box=\"([^\"]*)\"")]
    private static partial Regex BoxedBlock();

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

                // The drawing's type, from its own svg, before the pre closes.
                int end = html.IndexOf("</pre>", close, StringComparison.Ordinal);
                int role = html.IndexOf("aria-roledescription=\"", close, StringComparison.Ordinal);

                if (role > 0 && (end < 0 || role < end))
                {
                    int from = role + "aria-roledescription=\"".Length;
                    int to = html.IndexOf('"', from);

                    if (to > from)
                    {
                        _roles[hash] = html[from..to];
                    }
                }
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

                // The equation's numbers, which the shell wrote onto KaTeX's visible half
                // (numberEquations in app.js) - after this equation's MathML, before the next.
                int next = html.IndexOf("<math", end, StringComparison.Ordinal);
                string after = next < 0 ? html[end..] : html[end..next];

                if (EquationNumber().Matches(after) is { Count: > 0 } numbers)
                {
                    _numbers[(line, ordinal)] = [.. numbers.Select(n => WebUtility.HtmlDecode(n.Groups[1].Value))];
                }

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
