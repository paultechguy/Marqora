// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Figures;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Yaml;
using Markdig.Helpers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Domain;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;
using MarkdigFootnote = Markdig.Extensions.Footnotes.Footnote;
using MarkdigTable = Markdig.Extensions.Tables.Table;
using MarkdigTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdigTableRow = Markdig.Extensions.Tables.TableRow;
using WordTable = DocumentFormat.OpenXml.Wordprocessing.Table;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// Walks the parsed document and appends what each block becomes to the Word body.
///
/// The order of the cases in <see cref="Write(Block)"/> is not arbitrary and not alphabetical.
/// Three of Markdig's types derive from another type this walker also handles, so a case for
/// the base type placed first silently swallows them:
///
/// <list type="bullet">
///   <item>AlertBlock is a QuoteBlock - every GitHub callout becomes a plain block quote.</item>
///   <item>MathBlock is a FencedCodeBlock - every display equation becomes a code box.</item>
///   <item>YamlFrontMatterBlock is a CodeBlock - front matter gets printed at the top of the
///   document instead of becoming its properties.</item>
/// </list>
///
/// Only the third of those is caught by the compiler, and only because nothing else here
/// wants front matter; the other two produce a document that opens, reads almost right, and
/// is wrong. That is why the ordering is written down rather than left to look accidental.
/// </summary>
internal sealed class BlockRenderer
{
    private readonly Body _body;
    private readonly InlineRenderer _inlines;
    private readonly BookmarkTable _bookmarks;
    private readonly NumberingPlan _numbering;
    private readonly int _usableWidthTwips;
    private readonly DocxImages _images;
    private readonly DocxFootnotes _footnotes;
    private readonly PreviewHarvest _preview;
    private readonly IReadOnlyDictionary<string, byte[]> _diagrams;
    private readonly ExportReport _report;
    private readonly ILogger _logger;

    /// <summary>Fence languages the preview draws as pictures rather than as code.</summary>
    private static readonly string[] DiagramLanguages = ["mermaid"];

    public BlockRenderer(
        Body body,
        MainDocumentPart main,
        BookmarkTable bookmarks,
        NumberingPlan numbering,
        int usableWidthTwips,
        string? sourceDocumentPath,
        ExportReport report,
        PreviewHarvest preview,
        IReadOnlyDictionary<string, byte[]> diagrams,
        ILogger logger)
    {
        _body = body;
        _bookmarks = bookmarks;
        _numbering = numbering;
        _usableWidthTwips = usableWidthTwips;
        _preview = preview;
        _diagrams = diagrams;
        _report = report;
        _logger = logger;
        _images = new DocxImages(main, sourceDocumentPath, report, logger);
        _footnotes = new DocxFootnotes(main);
        _inlines = new InlineRenderer(
            main, bookmarks, _images, _footnotes, preview, report, usableWidthTwips, logger);
    }

    /// <summary>
    /// Collects the bookmark name of every heading before anything is written.
    ///
    /// It has to happen first because a link may point at a heading further down the
    /// document, and the walker only knows a name is legal once it has seen the heading it
    /// belongs to. Doing this in one pass up front is what lets a forward link resolve.
    /// </summary>
    public void CollectAnchors(MarkdigDocument document)
    {
        foreach (HeadingBlock heading in document.Descendants<HeadingBlock>())
        {
            if (heading.GetAttributes().Id is { Length: > 0 } id)
            {
                _bookmarks.NameFor(id);
            }
        }
    }

    public void WriteAll(MarkdigDocument document)
    {
        foreach (Block block in document)
        {
            Write(block);
        }

        // The footnotes part is attached only once something has asked for a note, and only
        // after the walk: a reference met in the body allocates the id, and the body that goes
        // with it is written when the walk reaches the group at the end.
        _footnotes.Save();
    }

    private void Write(Block block)
    {
        switch (block)
        {
            // ---- must precede the types they derive from ----

            // MathBlock derives from FencedCodeBlock. Matched after it, every display
            // equation in the document would be written as a shaded code box.
            case MathBlock math:
                WriteDisplayMath(math);
                break;

            // AlertBlock derives from QuoteBlock. Matched after it, all five GitHub callouts
            // would come out as plain block quotes - which looks deliberate, and is not.
            case AlertBlock alert:
                WriteCallout(alert);
                break;

            // YamlFrontMatterBlock derives from CodeBlock - which is not obvious, and is the
            // one of these three the compiler will catch, because the case below would be
            // unreachable rather than merely wrong. Front matter is the document's metadata
            // and becomes its Word properties; matched after CodeBlock it would instead be
            // printed at the top of every document that has any, in a shaded box.
            case YamlFrontMatterBlock:
                break;

            // ---- ordinary blocks ----

            case HeadingBlock heading:
                WriteHeading(heading);
                break;

            case ParagraphBlock paragraph:
                WriteParagraph(paragraph);
                break;

            case ListBlock list:
                WriteList(list);
                break;

            case MarkdigTable table:
                WriteTable(table);
                break;

            case QuoteBlock quote:
                WriteQuote(quote);
                break;

            case FencedCodeBlock fenced when IsDiagram(fenced):
                WriteDiagram(fenced);
                break;

            case FencedCodeBlock fenced:
                WriteCode(fenced);
                break;

            case CodeBlock code:
                WriteCode(code);
                break;

            case DefinitionList definitions:
                WriteDefinitionList(definitions);
                break;

            case FigureCaption caption:
                WriteStyled(caption.Inline, StyleIds.Caption);
                break;

            case ThematicBreakBlock:
                WriteThematicBreak();
                break;

            // Every footnote in the document is collected here, at the end, because that is
            // where Markdig moves the definitions to. It is walked rather than skipped: the
            // definitions are relocated, not copied, so skipping this loses all of them.
            case FootnoteGroup group:
                WriteFootnotes(group);
                break;

            // Link definitions are invisible in every renderer. Left to the default branch
            // they would be walked as ordinary containers and their text would appear.
            case LinkReferenceDefinitionGroup:
                break;

            case HtmlBlock:
                _logger.LogDebug("Raw HTML block dropped; Word has no equivalent.");
                break;

            case ContainerBlock container:
                foreach (Block child in container)
                {
                    Write(child);
                }

                break;

            default:
                _logger.LogDebug("No Word equivalent for block {Block}; dropped.", block.GetType().Name);
                break;
        }
    }

    /// <summary>
    /// A heading, with the bookmark that makes it linkable and the section number when the
    /// reader has asked for numbering.
    ///
    /// The bookmark brackets the whole paragraph rather than the text, so that a link lands on
    /// the heading and Word's navigation pane has something to select.
    /// </summary>
    private void WriteHeading(HeadingBlock heading)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = StyleIds.Heading(heading.Level) }));

        string? id = heading.GetAttributes().Id;
        int bookmarkId = -1;

        if (id is { Length: > 0 })
        {
            bookmarkId = _bookmarks.NextId();

            paragraph.AppendChild(new BookmarkStart
            {
                Id = bookmarkId.ToString(Invariant),
                Name = _bookmarks.NameFor(id),
            });
        }

        // No number is written here. It used to be - as ordinary text, the way the preview
        // writes it - and that was wrong for a document somebody is going to edit: the numbers
        // were correct when the file was written and stale from the first inserted section.
        // They come from the heading style's own numbering now, which Word maintains.
        _inlines.Write(heading.Inline, paragraph, default, heading.Line);

        if (bookmarkId >= 0)
        {
            paragraph.AppendChild(new BookmarkEnd { Id = bookmarkId.ToString(Invariant) });
        }

        _body.AppendChild(paragraph);
    }

    private void WriteParagraph(ParagraphBlock block)
    {
        var paragraph = new Paragraph();

        _inlines.Write(block.Inline, paragraph, default, block.Line);

        _body.AppendChild(paragraph);
    }

    /// <summary>
    /// A list, and whatever is nested inside its items.
    ///
    /// The first paragraph of each item carries the numbering; anything after it - a second
    /// paragraph, a code block, a nested table - carries only the indent, so it lines up under
    /// the item's text rather than restarting the count. A nested list is written by this same
    /// method and finds its own deeper level in the plan.
    /// </summary>
    private void WriteList(ListBlock list)
    {
        bool numbered = _numbering.TryGet(list, out ListPlacement placement);

        foreach (Block child in list)
        {
            if (child is not ListItemBlock item)
            {
                continue;
            }

            // Asked of the item, not of the list: a list may hold a ticked item and a plain
            // one side by side, and only the ticked one has to give up its marker.
            bool task = NumberingPlan.IsTaskItem(item);
            bool first = true;

            foreach (Block content in item)
            {
                if (content is ListBlock nested)
                {
                    WriteList(nested);
                    continue;
                }

                int before = _body.ChildElements.Count;

                Write(content);

                for (int i = before; i < _body.ChildElements.Count; i++)
                {
                    if (_body.ChildElements[i] is not Paragraph paragraph)
                    {
                        continue;
                    }

                    ApplyListFormatting(paragraph, placement, numbered, task, marker: first);
                    first = false;
                }
            }
        }
    }

    /// <summary>
    /// Puts one paragraph into a list.
    ///
    /// A task item gets indentation and no numbering at all: Word defines a marker once per
    /// level rather than per item, so a ticked box and an empty one cannot come from the same
    /// definition. The box itself is written by the inline walker, as text. Its plain
    /// siblings are untouched and keep their bullets.
    ///
    /// Contextual spacing is what makes a tight list look tight. Without it Word puts its
    /// usual paragraph gap between every item and a five-item list reads as double-spaced;
    /// with it the gap survives before and after the list but not inside it. A loose list -
    /// one with blank lines between its items in the source - deliberately keeps the gaps.
    /// </summary>
    private static void ApplyListFormatting(
        Paragraph paragraph,
        ListPlacement placement,
        bool numbered,
        bool task,
        bool marker)
    {
        ParagraphProperties properties = EnsureProperties(paragraph);

        // A code block inside a list item keeps its own style; only ordinary paragraphs are
        // restyled as list text.
        if (properties.ParagraphStyleId is null)
        {
            properties.ParagraphStyleId = new ParagraphStyleId { Val = StyleIds.ListParagraph };
        }

        if (numbered && !task && marker)
        {
            // The level comes before the instance, which is the schema's order and not the
            // one that reads naturally.
            properties.AppendChild(new NumberingProperties(
                new NumberingLevelReference { Val = placement.Level },
                new NumberingId { Val = placement.NumberId }));
        }
        else
        {
            // A task item hangs its box out where a bullet would have gone, so that a ticked
            // item and a plain one beside it line their text up on the same edge. A second
            // paragraph inside an item has no marker to hang and sits square under the text
            // above it.
            var indent = new Indentation
            {
                Left = ((placement.Level + 1) * NumberingPlan.IndentPerLevel)
                    .ToString(Invariant),
            };

            if (task && marker)
            {
                indent.Hanging = NumberingPlan.IndentPerLevel.ToString(Invariant);
            }

            properties.AppendChild(indent);
        }

        if (placement.Tight)
        {
            properties.AppendChild(new ContextualSpacing());
        }
    }

    /// <summary>
    /// One of the five GitHub callouts.
    ///
    /// The label paragraph comes first and is a style of its own, so that "Warning" is bold
    /// and amber above the body rather than merged into the first sentence. Everything after
    /// it is written as it would be anywhere and then restyled, which is what lets a callout
    /// hold a list, a fence or a table.
    /// </summary>
    private void WriteCallout(AlertBlock alert)
    {
        CalloutKind kind = CalloutColors.Parse(alert.Kind.ToString());

        _body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new ParagraphStyleId { Val = StyleIds.CalloutTitle(kind) }),
            default(RunFormat).ToRun(CalloutColors.TitleOf(kind))));

        int before = _body.ChildElements.Count;

        foreach (Block child in alert)
        {
            Write(child);
        }

        // A callout with nothing in it still needs a body paragraph, or the label sits on a
        // panel with no bottom to it.
        if (_body.ChildElements.Count == before)
        {
            _body.AppendChild(new Paragraph());
        }

        for (int i = before; i < _body.ChildElements.Count; i++)
        {
            if (_body.ChildElements[i] is Paragraph paragraph
                && EnsureProperties(paragraph).ParagraphStyleId is null)
            {
                EnsureProperties(paragraph).ParagraphStyleId =
                    new ParagraphStyleId { Val = StyleIds.Callout(kind) };
            }
        }
    }

    /// <summary>
    /// A definition list: a term, then one or more definitions indented under it.
    ///
    /// Word has no definition list. Two paragraph styles carry the shape, which is what every
    /// word processor does with one and what a reader would have typed by hand.
    /// </summary>
    private void WriteDefinitionList(DefinitionList list)
    {
        foreach (Block child in list)
        {
            if (child is not DefinitionItem item)
            {
                Write(child);
                continue;
            }

            // The term is inside the item rather than beside it - the tree is
            // DefinitionList > DefinitionItem > [DefinitionTerm, the definition] - so both
            // are found here. Looking for the term a level up finds nothing and drops it,
            // silently, leaving a document of definitions with nothing being defined.
            foreach (Block content in item)
            {
                if (content is DefinitionTerm term)
                {
                    WriteStyled(term.Inline, StyleIds.DefinitionTerm);
                    continue;
                }

                int before = _body.ChildElements.Count;

                Write(content);

                for (int i = before; i < _body.ChildElements.Count; i++)
                {
                    if (_body.ChildElements[i] is Paragraph paragraph
                        && EnsureProperties(paragraph).ParagraphStyleId is null)
                    {
                        EnsureProperties(paragraph).ParagraphStyleId =
                            new ParagraphStyleId { Val = StyleIds.DefinitionItem };
                    }
                }
            }
        }
    }

    /// <summary>
    /// The footnote bodies, written into the footnotes part rather than into the page.
    ///
    /// Word numbers them itself: the reference in the text and the mark in front of the note
    /// are both fields, which is why a document stays correct when somebody inserts a
    /// paragraph in the middle of it.
    /// </summary>
    private void WriteFootnotes(FootnoteGroup group)
    {
        foreach (MarkdigFootnote note in group.OfType<MarkdigFootnote>())
        {
            _footnotes.Write(note, WriteInto);
        }
    }

    /// <summary>
    /// Runs the block walker into somewhere other than the body, and hands back what it
    /// produced. Footnote bodies and table cells both need this: the walker appends to the
    /// body as it goes, so the elements are lifted back out and moved where they belong.
    /// </summary>
    private List<OpenXmlElement> WriteInto(IEnumerable<Block> blocks)
    {
        int before = _body.ChildElements.Count;

        foreach (Block block in blocks)
        {
            Write(block);
        }

        var moved = new List<OpenXmlElement>();

        for (int i = _body.ChildElements.Count - 1; i >= before; i--)
        {
            OpenXmlElement element = _body.ChildElements[i];

            element.Remove();
            moved.Insert(0, element);
        }

        return moved;
    }

    private void WriteStyled(ContainerInline? inline, string styleId)
    {
        var paragraph = new Paragraph(
            new ParagraphProperties(new ParagraphStyleId { Val = styleId }));

        _inlines.Write(inline, paragraph, default);

        _body.AppendChild(paragraph);
    }

    /// <summary>
    /// A pipe or grid table.
    ///
    /// Two rules about what surrounds it. Every cell needs at least an empty paragraph inside
    /// it, or Word calls the file damaged. And a table must be followed by a paragraph: two
    /// tables with nothing between them are merged into one, which turns a document with two
    /// tables into a document with one very confusing one.
    /// </summary>
    private void WriteTable(MarkdigTable table)
    {
        int columns = DocxTables.ColumnCount(table);

        if (columns == 0)
        {
            return;
        }

        int[] widths = DocxTables.Widths(table, _usableWidthTwips);

        var word = new WordTable(DocxTables.Properties(), DocxTables.Grid(widths));

        foreach (MarkdigTableRow row in table.OfType<MarkdigTableRow>())
        {
            word.AppendChild(WriteRow(table, row, widths, columns));
        }

        _body.AppendChild(word);
        _body.AppendChild(new Paragraph());
    }

    private TableRow WriteRow(
        MarkdigTable table,
        MarkdigTableRow row,
        int[] widths,
        int columns)
    {
        var wordRow = new TableRow();

        if (row.IsHeader)
        {
            // The order is the schema's: cantSplit before tblHeader. Repeating the header on
            // each page only works on a leading run of rows, which is where a markdown
            // table's header always is.
            wordRow.AppendChild(new TableRowProperties(
                new CantSplit(),
                new TableHeader()));
        }

        int column = 0;

        foreach (MarkdigTableCell cell in row.OfType<MarkdigTableCell>())
        {
            int at = cell.ColumnIndex >= 0 ? cell.ColumnIndex : column;
            int span = Math.Max(1, cell.ColumnSpan);

            wordRow.AppendChild(WriteCell(table, cell, widths, at, span));

            column = at + span;
        }

        // A markdown row is allowed to be short; a Word row is not, and the missing cells
        // would otherwise be drawn as one wide cell running off the end of the grid.
        for (; column < columns; column++)
        {
            wordRow.AppendChild(new TableCell(
                new TableCellProperties(new TableCellWidth
                {
                    Width = "0",
                    Type = TableWidthUnitValues.Auto,
                }),
                new Paragraph()));
        }

        return wordRow;
    }

    private TableCell WriteCell(
        MarkdigTable table,
        MarkdigTableCell cell,
        int[] widths,
        int column,
        int span)
    {
        // A spanning cell is as wide as the columns it covers, or the grid and the cells stop
        // agreeing and Word re-fits the table to neither.
        // Auto, like the table itself: a stated width would be the guess the table is no
        // longer making, and Word would honour it over its own measurement.
        var properties = new TableCellProperties(new TableCellWidth
        {
            Width = "0",
            Type = TableWidthUnitValues.Auto,
        });

        if (span > 1)
        {
            properties.AppendChild(new GridSpan { Val = span });
        }

        var wordCell = new TableCell(properties);

        JustificationValues? alignment = DocxTables.AlignmentOf(table, column);

        int before = _body.ChildElements.Count;

        foreach (Block child in cell)
        {
            Write(child);
        }

        // The walker writes into the body, so what it produced for this cell is lifted back
        // out and moved inside. Doing it this way means a cell can hold anything a document
        // can - a list, a fence, a nested quote - rather than only a line of text.
        var moved = new List<OpenXmlElement>();

        for (int i = _body.ChildElements.Count - 1; i >= before; i--)
        {
            OpenXmlElement element = _body.ChildElements[i];

            element.Remove();
            moved.Insert(0, element);
        }

        foreach (OpenXmlElement element in moved)
        {
            if (alignment is { } value && element is Paragraph paragraph)
            {
                // Word aligns cells, not columns, so a column's alignment is written onto
                // every paragraph in it.
                EnsureProperties(paragraph).AppendChild(new Justification { Val = value });
            }

            wordCell.AppendChild(element);
        }

        if (!wordCell.Elements<Paragraph>().Any())
        {
            wordCell.AppendChild(new Paragraph());
        }

        return wordCell;
    }

    /// <summary>
    /// A block quote, and anything nested inside one.
    ///
    /// Word has no container for a quote - only a paragraph style - so the children are
    /// written as they would be anywhere and the style is applied to each paragraph
    /// afterwards. Nesting adds indentation per level, because the style's left bar is drawn
    /// once however deep the quote goes.
    /// </summary>
    private void WriteQuote(QuoteBlock quote)
    {
        int before = _body.ChildElements.Count;

        foreach (Block child in quote)
        {
            Write(child);
        }

        for (int i = before; i < _body.ChildElements.Count; i++)
        {
            if (_body.ChildElements[i] is not Paragraph paragraph)
            {
                continue;
            }

            ParagraphProperties properties = EnsureProperties(paragraph);

            // A paragraph that already carries a style came from something with a stronger
            // claim to it - a heading or a code line inside the quote - and keeps it.
            if (properties.ParagraphStyleId is null)
            {
                properties.ParagraphStyleId = new ParagraphStyleId { Val = StyleIds.Quote };
            }
        }
    }

    /// <summary>
    /// A fenced or indented code block.
    ///
    /// Each source line becomes its own paragraph rather than one paragraph full of breaks.
    /// That is what lets Word collapse the identical borders of consecutive paragraphs into a
    /// single frame around the whole fence - see the style - and it keeps a long line's wrap
    /// behaving like a line of code rather than like prose.
    /// </summary>
    /// <summary>
    /// Whether a fence is a diagram rather than code.
    ///
    /// The same list the preview renderer uses to decide the same thing, so a language that
    /// becomes a picture on screen becomes a picture here.
    /// </summary>
    private static bool IsDiagram(FencedCodeBlock block) =>
        block.Info is { Length: > 0 } info
        && DiagramLanguages.Contains(info, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A mermaid diagram, as the picture the shell drew.
    ///
    /// There is no way to draw one here: a definition becomes a diagram only after mermaid has
    /// laid it out, and that needs the browser. So the shell is asked for the picture before
    /// the walk starts and it arrives here already rendered.
    ///
    /// When there is no picture - the preview never ran, or the definition did not parse - the
    /// source is written as a code block. That is worse than a diagram and much better than a
    /// gap: the reader can see what was meant, and the document still says everything the
    /// markdown said.
    /// </summary>
    private void WriteDiagram(FencedCodeBlock block)
    {
        if (_preview.TryDiagram(block.Line, out string hash)
            && _diagrams.TryGetValue(hash, out byte[]? png)
            && _images.TryBuildFromBytes(png, "Diagram", _usableWidthTwips, scale: 2) is { } run)
        {
            // Room to breathe, the same amount on each side. The preview sets a diagram
            // apart with a padded panel in a different color; Word gets the picture on its
            // own, so the separation has to come from the spacing alone or the diagram reads
            // as part of the paragraph under it.
            //
            // These were briefly 240 above and 360 below, on the reasoning that the block
            // before the diagram contributes its own space-after to the gap above while
            // nothing contributes to the gap below. Whatever Word does with the two figures,
            // it is not that: the smaller number came out as the visibly smaller gap. Equal
            // figures, equal gaps.
            //
            // Spacing before justification: w:pPr is a schema sequence and jc comes after the
            // spacing and indent, not before them.
            _body.AppendChild(new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "360", After = "360" },
                    new Justification { Val = JustificationValues.Center }),
                run));

            return;
        }

        _logger.LogDebug(
            "No picture for the diagram at line {Line}; writing its source instead.",
            block.Line);

        WriteCode(block);
    }

    /// <summary>
    /// A fence, and the seam that stops it joining the next one.
    ///
    /// Word treats consecutive paragraphs carrying identical borders as one block and draws a
    /// single frame around the lot. That is what turns the lines of one fence into one box -
    /// it is deliberate, and the callouts lean on the same behavior - but it has no idea where
    /// one fence ends and the next begins. Two of them back to back, which is exactly how the
    /// cheatsheet shows a fence inside a fence, came out welded into a single box with no edge
    /// between them and no air above the second.
    ///
    /// An empty paragraph breaks the group, because it carries no borders to match. It is a
    /// point high so that it reads as a seam rather than as a blank line, and it is written
    /// after every fence rather than only between two: knowing what comes next would mean
    /// looking ahead, and the cost of always is one point.
    /// </summary>
    private void WriteCode(LeafBlock block)
    {
        WriteCodeLines(block);

        _body.AppendChild(new Paragraph(new ParagraphProperties(
            new SpacingBetweenLines
            {
                Before = "0",
                After = "0",
                Line = "20",
                LineRule = LineSpacingRuleValues.Exact,
            })));
    }

    private void WriteCodeLines(LeafBlock block)
    {
        var properties = new ParagraphProperties(
            new ParagraphStyleId { Val = StyleIds.CodeBlock });

        StringLineGroup lines = block.Lines;

        if (lines.Count == 0)
        {
            _body.AppendChild(new Paragraph(properties.CloneNode(true)));
            return;
        }

        string[] source = new string[lines.Count];

        for (int i = 0; i < lines.Count; i++)
        {
            source[i] = lines.Lines[i].Slice.ToString();
        }

        if (ColoredCode(block, source) is { } colored)
        {
            foreach (List<CodeToken> line in colored)
            {
                var paragraph = new Paragraph(properties.CloneNode(true));

                foreach (CodeToken token in line)
                {
                    paragraph.AppendChild(CodeRun(token));
                }

                _body.AppendChild(paragraph);
            }

            return;
        }

        foreach (string line in source)
        {
            var paragraph = new Paragraph(properties.CloneNode(true));

            paragraph.AppendChild(default(RunFormat).ToRun(line));

            _body.AppendChild(paragraph);
        }
    }

    /// <summary>
    /// The block's tokens as the preview colored them, split back into lines - or null when
    /// there are none to be had, or when the ones on offer are stale.
    ///
    /// The comparison is the point. The editor runs ahead of the preview by a debounce
    /// interval, so a fence edited a moment before the export comes back with exactly the
    /// right shape and the wrong text; writing it would put yesterday's code in the document
    /// and say nothing. Comparing what the preview returned against what the tree holds costs
    /// one string comparison and makes the whole arrangement exact rather than usually right.
    /// </summary>
    private List<List<CodeToken>>? ColoredCode(LeafBlock block, string[] source)
    {
        if (!_preview.TryCode(block.Line, out IReadOnlyList<CodeToken> tokens))
        {
            return null;
        }

        string harvested = string.Concat(tokens.Select(t => t.Text));
        string expected = string.Join('\n', source);

        // Markdig drops the fence's trailing newline; the markup keeps it.
        if (harvested.TrimEnd('\n', '\r') != expected.TrimEnd('\n', '\r'))
        {
            _logger.LogDebug(
                "The preview's colors for the code at line {Line} are out of date; writing it plain.",
                block.Line);

            return null;
        }

        var lines = new List<List<CodeToken>> { new() };

        foreach (CodeToken token in tokens)
        {
            string[] parts = token.Text.Split('\n');

            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    lines.Add([]);
                }

                if (parts[i].Length > 0)
                {
                    lines[^1].Add(token with { Text = parts[i] });
                }
            }
        }

        // A fence ends with a newline, which leaves one empty line behind it.
        if (lines.Count > source.Length && lines[^1].Count == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static Run CodeRun(CodeToken token)
    {
        var run = new Run();

        if (HighlightPalette.For(token.TokenClass) is { } style)
        {
            var properties = new RunProperties();

            if (style.Bold)
            {
                properties.AppendChild(new Bold());
            }

            if (style.Italic)
            {
                properties.AppendChild(new Italic());
            }

            properties.AppendChild(new Color { Val = style.Color });

            run.AppendChild(properties);
        }

        run.AppendChild(new Text(XmlSafeText.Clean(token.Text))
        {
            Space = SpaceProcessingModeValues.Preserve,
        });

        return run;
    }

    /// <summary>
    /// Display math, until the equation converter lands.
    ///
    /// The TeX source in the inline-code style, centered. It is the fallback the finished
    /// exporter will keep for an equation it cannot map, and it is honest in a way an empty
    /// space is not: a reader can paste it into Word's own equation editor.
    /// </summary>
    private void WriteDisplayMath(MathBlock math)
    {
        if (_preview.TryMath(math.Line, 0, out string mathml))
        {
            if (MathmlToOmml.Convert(mathml, out string? unsupported) is { } equation)
            {
                // An equation paragraph of its own, centered the way a display equation is set.
                _body.AppendChild(new Paragraph(
                    new DocumentFormat.OpenXml.Math.Paragraph(
                        new DocumentFormat.OpenXml.Math.ParagraphProperties(
                            new DocumentFormat.OpenXml.Math.Justification
                            {
                                Val = DocumentFormat.OpenXml.Math.JustificationValues.Center,
                            }),
                        equation)));

                return;
            }

            // The preview had an equation and the converter would not take it. Worth saying
            // which construct stopped it: that is how the converter grows to cover what real
            // documents actually contain.
            _report.UnsupportedMath(unsupported);
        }

        // No equation to be had: the preview has not run, or the expression uses something
        // the converter does not know. The TeX goes in instead - a reader can see what was
        // meant and can paste it into Word's own equation editor.
        string tex = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, math.Lines.Count).Select(i => math.Lines.Lines[i].Slice.ToString()));

        _body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new Justification { Val = JustificationValues.Center }),
            default(RunFormat).WithCharacterStyle(StyleIds.CodeChar).ToRun(tex)));
    }

    /// <summary>
    /// A horizontal rule, drawn as a bottom border on an empty paragraph.
    ///
    /// Word has no rule element. A paragraph with a bottom border is what Word itself writes
    /// when a user types three hyphens and lets AutoFormat turn them into a line.
    /// </summary>
    private void WriteThematicBreak() =>
        _body.AppendChild(new Paragraph(
            new ParagraphProperties(
                new ParagraphBorders(
                    new BottomBorder
                    {
                        Val = BorderValues.Single,
                        Size = 6U,
                        Space = 1U,
                        Color = "D0D0D0",
                    }),
                new SpacingBetweenLines { Before = "240", After = "240" })));

    private static ParagraphProperties EnsureProperties(Paragraph paragraph)
    {
        if (paragraph.ParagraphProperties is { } existing)
        {
            return existing;
        }

        var properties = new ParagraphProperties();

        paragraph.InsertAt(properties, 0);

        return properties;
    }

    private static System.Globalization.CultureInfo Invariant =>
        System.Globalization.CultureInfo.InvariantCulture;
}
