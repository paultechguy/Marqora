// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Extensions.Abbreviations;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Logging;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// Walks the inlines of one block and appends the runs they become to a paragraph.
///
/// Everything a run can wear is carried down in a <see cref="RunFormat"/> rather than emitted
/// on the way in, because markdown nests what Word flattens - see that type for why.
/// </summary>
internal sealed class InlineRenderer
{
    private readonly MainDocumentPart _main;
    private readonly BookmarkTable _bookmarks;
    private readonly DocxImages _images;
    private readonly DocxFootnotes _footnotes;
    private readonly PreviewHarvest _preview;
    private readonly ExportReport _report;

    /// <summary>The line of the block being walked, and how many equations it has held.</summary>
    private int _sourceLine = -1;
    private int _mathOrdinal;

    /// <summary>Inline HTML currently open, innermost last.</summary>
    private readonly Stack<RunFormat> _htmlFormat = new();

    /// <summary>Abbreviations already spelled out, so each is expanded once.</summary>
    private readonly HashSet<string> _expandedAbbreviations = new(StringComparer.Ordinal);
    private readonly int _maximumImageWidthTwips;
    private readonly ILogger _logger;

    public InlineRenderer(
        MainDocumentPart main,
        BookmarkTable bookmarks,
        DocxImages images,
        DocxFootnotes footnotes,
        PreviewHarvest preview,
        ExportReport report,
        int maximumImageWidthTwips,
        ILogger logger)
    {
        _main = main;
        _bookmarks = bookmarks;
        _images = images;
        _footnotes = footnotes;
        _preview = preview;
        _report = report;
        _maximumImageWidthTwips = maximumImageWidthTwips;
        _logger = logger;
    }

    /// <param name="sourceLine">
    /// The line the containing block came from, which is how an equation inside it is found
    /// in what the preview handed back. Several equations can share one paragraph, so they
    /// are counted as they are met - and the count restarts here, with the block.
    /// </param>
    public void Write(
        ContainerInline? container,
        Paragraph paragraph,
        RunFormat format,
        int sourceLine = -1)
    {
        if (container is null)
        {
            return;
        }

        if (sourceLine >= 0)
        {
            _sourceLine = sourceLine;
            _mathOrdinal = 0;
        }

        foreach (Inline inline in container)
        {
            Write(inline, paragraph, format);
        }
    }

    private void Write(Inline inline, Paragraph paragraph, RunFormat format)
    {
        // Inline HTML is written as it is read, so the tags arrive either side of the text
        // rather than wrapped round it. Anything opened is carried on this stack until it is
        // closed, and folded into whatever the surrounding markdown already asked for.
        format = _htmlFormat.Count > 0 ? Combine(format, _htmlFormat.Peek()) : format;

        switch (inline)
        {
            case LiteralInline literal:
                Append(paragraph, format.ToRun(literal.Content.ToString()));
                break;

            case EmphasisInline emphasis:
                Write(emphasis, paragraph, Apply(emphasis, format));
                break;

            case CodeInline code:
                Append(
                    paragraph,
                    format.WithCharacterStyle(StyleIds.CodeChar).ToRun(code.Content ?? string.Empty));
                break;

            case LinkInline { IsImage: true } image:
                WriteImage(image, paragraph, format);
                break;

            case LinkInline link:
                WriteLink(link, paragraph, format);
                break;

            case AutolinkInline autolink:
                WriteAutolink(autolink, paragraph, format);
                break;

            case LineBreakInline { IsHard: true }:
                paragraph.AppendChild(new Run(new Break()));
                break;

            case LineBreakInline:
                // A soft break is a newline in the source and a space in the output, which is
                // what every other markdown renderer does with it.
                Append(paragraph, format.ToRun(" "));
                break;

            case TaskList task:
                Append(paragraph, TaskGlyph(task, format));
                paragraph.AppendChild(new Run(new TabChar()));
                break;

            case HtmlEntityInline entity:
                Append(paragraph, format.ToRun(entity.Transcoded.ToString()));
                break;

            // The back-link is the little arrow the preview puts at the end of a note so a
            // reader can get back to where they were. Word does that itself - double-clicking
            // a note jumps to its reference - so writing one would be a second, worse copy.
            case FootnoteLink { IsBackLink: false } footnote:
                paragraph.AppendChild(_footnotes.Reference(footnote.Footnote));
                break;

            case FootnoteLink:
                break;

            case AbbreviationInline abbreviation:
                WriteAbbreviation(abbreviation, paragraph, format);
                break;

            case MathInline math:
                WriteInlineMath(math, paragraph, format);
                break;

            case HtmlInline html:
                ReadHtmlTag(html.Tag, paragraph);
                break;

            case ContainerInline container:
                Write(container, paragraph, format);
                break;

            default:
                _logger.LogDebug("No Word equivalent for inline {Inline}; dropped.", inline.GetType().Name);
                break;
        }
    }

    /// <summary>
    /// What a run of emphasis means.
    ///
    /// The delimiter and its count are how Markdig distinguishes the six, and the mapping is
    /// its own: two asterisks or underscores is strong, one is emphasis; two tildes is a
    /// strikethrough and one is a subscript; a caret is superscript, a plus is inserted text
    /// and an equals sign is a highlight. Inserted text becomes an underline, which is the
    /// closest Word has - a tracked-change insertion would be a different claim entirely.
    /// </summary>
    private static RunFormat Apply(EmphasisInline emphasis, RunFormat format) =>
        emphasis.DelimiterChar switch
        {
            '*' or '_' => emphasis.DelimiterCount >= 2 ? format.WithBold() : format.WithItalic(),
            '~' => emphasis.DelimiterCount >= 2 ? format.WithStrike() : format.WithSubscript(),
            '^' => format.WithSuperscript(),
            '+' => format.WithUnderline(),
            '=' => format.WithCharacterStyle(StyleIds.Mark),
            _ => format,
        };

    /// <summary>
    /// A picture, or the alt text when there is no picture to be had.
    ///
    /// The fallback is deliberately visible rather than silent. An image that is missing, or
    /// remote, or in a format Word will not draw, leaves the document with a gap; writing the
    /// alt text in its place means a reader can tell what was meant to be there, and the
    /// caller is separately told so it can say which ones were left out.
    /// </summary>
    private void WriteImage(LinkInline image, Paragraph paragraph, RunFormat format)
    {
        string alt = AltTextOf(image);

        if (_images.TryBuild(image.Url ?? string.Empty, alt, _maximumImageWidthTwips) is { } run)
        {
            paragraph.AppendChild(run);
            return;
        }

        Append(paragraph, format.WithItalic().ToRun(alt.Length > 0 ? $"[{alt}]" : "[image]"));
    }

    /// <summary>
    /// An opening or closing inline HTML tag.
    ///
    /// A tag Word has no form of - and there are many - is stepped over rather than dropping
    /// what it surrounds: the content still reaches the document, only the formatting is lost,
    /// which is the right way round. A closing tag for something never opened is ignored, so a
    /// document with unbalanced markup cannot unwind the stack past the bottom.
    /// </summary>
    private void ReadHtmlTag(string tag, Paragraph paragraph)
    {
        if (InlineHtml.Parse(tag) is not { } parsed)
        {
            return;
        }

        // A line break is the one inline tag that is content rather than formatting.
        if (parsed.Name == "br")
        {
            paragraph.AppendChild(new Run(new Break()));
            return;
        }

        if (!InlineHtml.IsKnown(parsed.Name))
        {
            _logger.LogDebug("Inline <{Tag}> has no Word equivalent; its content is kept.", parsed.Name);
            return;
        }

        if (parsed.IsClosing)
        {
            if (_htmlFormat.Count > 0)
            {
                _htmlFormat.Pop();
            }

            return;
        }

        // A self-closing tag opens and shuts in one breath and has nothing to wrap.
        if (parsed.IsSelfClosing)
        {
            return;
        }

        RunFormat current = _htmlFormat.Count > 0 ? _htmlFormat.Peek() : default;

        _htmlFormat.Push(InlineHtml.Apply(parsed, current) ?? current);
    }

    /// <summary>
    /// Folds what the HTML asked for into what the markdown around it already said, so
    /// <c>**&lt;kbd&gt;Ctrl&lt;/kbd&gt;**</c> comes out bold and shaded rather than one or the other.
    /// </summary>
    private static RunFormat Combine(RunFormat markdown, RunFormat html) => markdown with
    {
        Bold = markdown.Bold || html.Bold,
        Italic = markdown.Italic || html.Italic,
        Strike = markdown.Strike || html.Strike,
        Underline = markdown.Underline || html.Underline,
        VerticalAlignment = markdown.VerticalAlignment ?? html.VerticalAlignment,
        CharacterStyle = markdown.CharacterStyle ?? html.CharacterStyle,
        Color = html.Color ?? markdown.Color,
        Shading = html.Shading ?? markdown.Shading,
    };

    /// <summary>
    /// An abbreviation, and its expansion the first time it appears.
    ///
    /// The preview shows the expansion as a tooltip, which paper and Word both lack. Putting
    /// it in parentheses on first use is what a written document does instead - and only on
    /// first use, or a page mentioning HTML eight times would say what it stands for eight
    /// times.
    /// </summary>
    private void WriteAbbreviation(
        AbbreviationInline abbreviation,
        Paragraph paragraph,
        RunFormat format)
    {
        string label = abbreviation.Abbreviation.Label ?? string.Empty;

        Append(paragraph, format.ToRun(label));

        if (!_expandedAbbreviations.Add(label))
        {
            return;
        }

        string expansion = abbreviation.Abbreviation.Text.ToString();

        if (expansion.Length > 0)
        {
            Append(paragraph, format.ToRun($" ({expansion})"));
        }
    }

    /// <summary>
    /// An equation in the middle of a sentence.
    ///
    /// It goes in as a real Word equation rather than as a picture or as its source, which is
    /// what lets a reader click into it, edit it, and have it set in the same face as the
    /// prose around it. The ordinal is what separates several equations sharing one paragraph:
    /// they all carry that paragraph's line.
    /// </summary>
    private void WriteInlineMath(MathInline math, Paragraph paragraph, RunFormat format)
    {
        int ordinal = _mathOrdinal++;

        if (_sourceLine >= 0 && _preview.TryMath(_sourceLine, ordinal, out string mathml))
        {
            if (MathmlToOmml.Convert(mathml, out string? unsupported) is { } equation)
            {
                paragraph.AppendChild(equation);
                return;
            }

            _report.UnsupportedMath(unsupported);
        }

        Append(
            paragraph,
            format.WithCharacterStyle(StyleIds.CodeChar).ToRun(math.Content.ToString()));
    }

    private void WriteLink(LinkInline link, Paragraph paragraph, RunFormat format)
    {
        string url = link.Url ?? string.Empty;

        // A link into this same document - "#some-heading" - is an anchor rather than an
        // address, and Word addresses those by bookmark name.
        if (url.StartsWith('#'))
        {
            WriteAnchor(link, url[1..], paragraph, format);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? target))
        {
            // A relative link to another file on disk. Word would write it as a path that
            // means nothing on the machine the document is read on, so the text stays and the
            // link does not.
            Write((ContainerInline)link, paragraph, format);
            return;
        }

        HyperlinkRelationship relationship =
            _main.AddHyperlinkRelationship(target, isExternal: true);

        var hyperlink = new Hyperlink { Id = relationship.Id, History = true };

        WriteInto(link, hyperlink, format.WithHyperlink());

        paragraph.AppendChild(hyperlink);
    }

    private void WriteAnchor(LinkInline link, string slug, Paragraph paragraph, RunFormat format)
    {
        if (!_bookmarks.Knows(slug))
        {
            // A link to a heading that is not in this document. The preview underlines it as
            // a dead link already, so writing it as a field that goes nowhere would only move
            // the problem into Word.
            Write((ContainerInline)link, paragraph, format);
            return;
        }

        var hyperlink = new Hyperlink { Anchor = _bookmarks.NameFor(slug), History = true };

        WriteInto(link, hyperlink, format.WithHyperlink());

        paragraph.AppendChild(hyperlink);
    }

    private void WriteAutolink(AutolinkInline autolink, Paragraph paragraph, RunFormat format)
    {
        string url = autolink.IsEmail ? $"mailto:{autolink.Url}" : autolink.Url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? target))
        {
            Append(paragraph, format.ToRun(autolink.Url));
            return;
        }

        HyperlinkRelationship relationship =
            _main.AddHyperlinkRelationship(target, isExternal: true);

        var hyperlink = new Hyperlink { Id = relationship.Id, History = true };

        hyperlink.AppendChild(format.WithHyperlink().ToRun(autolink.Url));

        paragraph.AppendChild(hyperlink);
    }

    /// <summary>
    /// A link's own label can be several inlines - a bold word inside the link text - so its
    /// children are walked into the hyperlink element rather than turned into one run.
    /// </summary>
    private void WriteInto(LinkInline link, Hyperlink hyperlink, RunFormat format)
    {
        var scratch = new Paragraph();

        Write(link, scratch, format);

        foreach (OpenXmlElement child in scratch.ChildElements.ToList())
        {
            hyperlink.AppendChild(child.CloneNode(true));
        }

        // A link whose label was empty still needs something clickable in it.
        if (!hyperlink.HasChildren)
        {
            hyperlink.AppendChild(format.ToRun(link.Url ?? string.Empty));
        }
    }

    /// <summary>
    /// The box in front of a task-list item.
    ///
    /// A literal character rather than numbering, because Word's list formats are defined per
    /// level and not per item: there is no way to say that the third bullet is ticked and the
    /// fourth is not. Segoe UI Symbol carries both glyphs on every supported version of
    /// Windows.
    /// </summary>
    private static Run TaskGlyph(TaskList task, RunFormat format)
    {
        Run run = format.ToRun(task.Checked ? "☒" : "☐");

        RunProperties properties = run.GetFirstChild<RunProperties>() ?? new RunProperties();

        if (run.GetFirstChild<RunProperties>() is null)
        {
            run.InsertAt(properties, 0);
        }

        properties.InsertAt(
            new RunFonts { Ascii = "Segoe UI Symbol", HighAnsi = "Segoe UI Symbol" },
            0);

        return run;
    }

    private static string AltTextOf(LinkInline image) =>
        string.Concat(image.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    private static void Append(Paragraph paragraph, Run run) => paragraph.AppendChild(run);
}
