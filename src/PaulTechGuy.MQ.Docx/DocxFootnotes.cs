// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Syntax;
using MarkdigFootnote = Markdig.Extensions.Footnotes.Footnote;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// The footnotes part, and the ids that tie a reference in the text to the note at the foot.
///
/// Two footnotes exist in every Word document that has any, and neither is a note: the
/// separator, which is the short rule Word draws above them, and the continuation separator,
/// which is the longer one it draws when a note runs over onto the next page. They live at
/// ids -1 and 0, they are not optional, and a part without them either draws no rule at all
/// or makes Word offer to repair the file.
///
/// Real notes are numbered from one. The number itself is never written: the mark in front of
/// the note is a field, and so is the reference in the text, which is what keeps them agreeing
/// after somebody inserts a paragraph in the middle of the document.
/// </summary>
internal sealed class DocxFootnotes
{
    private const int SeparatorId = -1;
    private const int ContinuationSeparatorId = 0;

    private readonly MainDocumentPart _main;
    private readonly Dictionary<MarkdigFootnote, int> _ids = [];

    private Footnotes? _footnotes;
    private int _nextId = 1;

    public DocxFootnotes(MainDocumentPart main) => _main = main;

    /// <summary>
    /// The id a footnote will be written under, allocated the first time it is asked for.
    ///
    /// References are met while walking the body and the bodies are written afterwards, so
    /// the id has to be handed out before the note itself exists.
    /// </summary>
    public int IdFor(MarkdigFootnote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        if (!_ids.TryGetValue(note, out int id))
        {
            id = _nextId++;
            _ids[note] = id;
        }

        return id;
    }

    /// <summary>
    /// Writes one note's body, using the caller to turn its blocks into Word elements.
    ///
    /// The walker belongs to the block renderer and this type has no business owning one, so
    /// it is passed in - which also means a footnote can hold anything a document can.
    /// </summary>
    public void Write(
        MarkdigFootnote note,
        Func<IEnumerable<Block>, List<OpenXmlElement>> render)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(render);

        int id = IdFor(note);

        var footnote = new Footnote { Id = id };

        List<OpenXmlElement> content = render(note);

        // The mark goes at the front of the first paragraph rather than in one of its own, so
        // a note reads as "1. The text" the way a printed footnote does.
        Paragraph first = content.OfType<Paragraph>().FirstOrDefault() ?? new Paragraph();

        if (!content.Contains(first))
        {
            content.Insert(0, first);
        }

        int at = first.ParagraphProperties is null ? 0 : 1;

        first.InsertAt(
            new Run(new RunProperties(new RunStyle { Val = StyleIds.FootnoteReference }), new FootnoteReferenceMark()),
            at);

        first.InsertAt(
            new Run(new Text(" ") { Space = SpaceProcessingModeValues.Preserve }),
            at + 1);

        foreach (OpenXmlElement element in content)
        {
            if (element is Paragraph paragraph
                && paragraph.ParagraphProperties?.ParagraphStyleId is null)
            {
                EnsureProperties(paragraph).ParagraphStyleId =
                    new ParagraphStyleId { Val = StyleIds.FootnoteText };
            }

            footnote.AppendChild(element);
        }

        Part().AppendChild(footnote);
    }

    /// <summary>The reference that goes in the body text, as a run.</summary>
    public Run Reference(MarkdigFootnote note) => new(
        new RunProperties(new RunStyle { Val = StyleIds.FootnoteReference }),
        new FootnoteReference { Id = IdFor(note) });

    /// <summary>
    /// Attaches the part to the document, if anything ever asked for a footnote.
    ///
    /// A document with no notes gets no part at all - an empty footnotes part is one more
    /// thing for Word to have an opinion about, and prose has no footnotes.
    /// </summary>
    public void Save()
    {
        if (_footnotes is null)
        {
            return;
        }

        _main.FootnotesPart!.Footnotes = _footnotes;
    }

    private Footnotes Part()
    {
        if (_footnotes is not null)
        {
            return _footnotes;
        }

        _main.AddNewPart<FootnotesPart>();

        _footnotes = new Footnotes(
            Separator(SeparatorId, new SeparatorMark()),
            Separator(ContinuationSeparatorId, new ContinuationSeparatorMark()));

        return _footnotes;
    }

    /// <summary>
    /// One of the two rules Word draws above the notes. The spacing is flattened because a
    /// rule with a paragraph gap under it leaves a visible step above the first note.
    /// </summary>
    private static Footnote Separator(int id, OpenXmlElement mark) => new(
        new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines
                {
                    After = "0",
                    Line = "240",
                    LineRule = LineSpacingRuleValues.Auto,
                }),
            new Run(mark)))
    {
        Id = id,
        Type = id == SeparatorId
            ? FootnoteEndnoteValues.Separator
            : FootnoteEndnoteValues.ContinuationSeparator,
    };

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
}
