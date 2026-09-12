// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// The style definitions the exported document carries.
///
/// Two rules run through all of it. Word's built-in styles are defined rather than left
/// latent, because a style a paragraph names but the file does not define is applied as
/// Normal without complaint - the failure is silent and looks like the walker's fault. And
/// colors and fonts are theme references rather than literals wherever Word has a slot for
/// them, because that is what makes the Design tab able to restyle the whole document.
/// </summary>
internal static class DocxStyles
{
    /// <summary>Body text, in half-points. 24 is 12pt, which is Word's own Aptos-era default.</summary>
    private const int BodyHalfPoints = 24;

    /// <summary>Code, a little smaller so a wide line has a chance of fitting.</summary>
    private const int CodeHalfPoints = 19;

    private const string CodeFont = "Cascadia Mono";
    private const string CodeInk = "24292E";
    private const string CodeFill = "F0F0F0";
    private const string CodeBorder = "E2E2E2";

    public static void Write(
        StyleDefinitionsPart part,
        HeadingNumbering headingNumbering = HeadingNumbering.Off,
        int? headingNumberId = null)
    {
        ArgumentNullException.ThrowIfNull(part);

        var styles = new Styles();

        styles.AppendChild(BuildDocDefaults());
        styles.AppendChild(BuildLatentStyles());

        styles.AppendChild(NormalStyle());
        styles.AppendChild(DefaultParagraphFontStyle());
        styles.AppendChild(TableNormalStyle());
        styles.AppendChild(NoListStyle());

        styles.AppendChild(TitleStyle());
        styles.AppendChild(SubtitleStyle());

        for (int level = 1; level <= 6; level++)
        {
            styles.AppendChild(HeadingStyle(level, headingNumbering, headingNumberId));
        }

        styles.AppendChild(QuoteStyle());
        styles.AppendChild(CaptionStyle());
        styles.AppendChild(ListParagraphStyle());
        styles.AppendChild(HyperlinkStyle());
        styles.AppendChild(CodeBlockStyle());
        styles.AppendChild(CodeCharStyle());
        styles.AppendChild(MarkStyle());
        styles.AppendChild(TableStyle());
        styles.AppendChild(DefinitionTermStyle());
        styles.AppendChild(DefinitionItemStyle());
        styles.AppendChild(FootnoteTextStyle());
        styles.AppendChild(FootnoteReferenceStyle());
        styles.AppendChild(RunningStyle(StyleIds.Header, "header"));
        styles.AppendChild(RunningStyle(StyleIds.Footer, "footer"));

        for (int level = 1; level <= 3; level++)
        {
            styles.AppendChild(ContentsEntryStyle(level));
        }

        styles.AppendChild(ContentsHeadingStyle(headingNumbering));

        foreach (CalloutKind kind in Enum.GetValues<CalloutKind>())
        {
            styles.AppendChild(CalloutStyle(kind));
            styles.AppendChild(CalloutTitleStyle(kind));
        }

        part.Styles = styles;
    }

    /// <summary>
    /// What every paragraph and run starts from before any style applies.
    ///
    /// The font is a theme reference so that swapping the theme swaps the document. The
    /// spacing is Word's own: 8pt after a paragraph, and 1.08 lines - which is what a line
    /// value of 278 means, 240 being single.
    /// </summary>
    private static DocDefaults BuildDocDefaults() => new(
        new RunPropertiesDefault(
            new RunPropertiesBaseStyle(
                MinorThemeFont(),
                new FontSize { Val = Text(BodyHalfPoints) },
                new FontSizeComplexScript { Val = Text(BodyHalfPoints) },
                new Languages { Val = "en-US" })),
        new ParagraphPropertiesDefault(
            new ParagraphPropertiesBaseStyle(
                new SpacingBetweenLines
                {
                    After = "160",
                    Line = "278",
                    LineRule = LineSpacingRuleValues.Auto,
                })));

    /// <summary>
    /// How Word should treat the several hundred styles this file never mentions.
    ///
    /// Without it the Styles gallery fills with everything Word knows about. With it, the
    /// defaults hide the lot and the exceptions below bring back the handful a reader of a
    /// markdown document would reach for.
    ///
    /// The trap is the vocabulary: the name here is the style's <em>built-in name</em>, not
    /// its id. It is "heading 1" and not "Heading1", "caption" in lower case, "Intense Quote"
    /// with a space. A name Word does not recognize is accepted and then ignored, so a typo
    /// costs nothing visible and quietly does nothing.
    /// </summary>
    private static LatentStyles BuildLatentStyles()
    {
        var latent = new LatentStyles
        {
            DefaultLockedState = false,
            DefaultUiPriority = 99,
            DefaultSemiHidden = true,
            DefaultUnhideWhenUsed = true,
            DefaultPrimaryStyle = false,
            Count = 376,
        };

        void Show(string builtInName, int priority, bool quickStyle = true) =>
            latent.AppendChild(new LatentStyleExceptionInfo
            {
                Name = builtInName,
                UiPriority = priority,
                SemiHidden = false,
                UnhideWhenUsed = false,
                PrimaryStyle = quickStyle,
            });

        Show("Normal", 0);

        for (int level = 1; level <= 6; level++)
        {
            Show($"heading {level}", 9);
        }

        Show("Title", 10);
        Show("Subtitle", 11);
        Show("Strong", 22);
        Show("Emphasis", 20);
        Show("Quote", 29);
        Show("Intense Quote", 30);
        Show("List Paragraph", 34);
        Show("caption", 35);

        for (int level = 1; level <= 3; level++)
        {
            Show($"toc {level}", 39, quickStyle: false);
        }

        Show("TOC Heading", 39, quickStyle: false);

        Show("Hyperlink", 99, quickStyle: false);
        Show("Table Grid", 39, quickStyle: false);

        return latent;
    }

    private static Style NormalStyle() => new(
        new StyleName { Val = "Normal" },
        new PrimaryStyle())
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.Normal,
        Default = true,
    };

    private static Style DefaultParagraphFontStyle() => new(
        new StyleName { Val = "Default Paragraph Font" },
        new UIPriority { Val = 1 },
        new SemiHidden(),
        new UnhideWhenUsed())
    {
        Type = StyleValues.Character,
        StyleId = StyleIds.DefaultParagraphFont,
        Default = true,
    };

    /// <summary>
    /// The table style everything else is based on. Word will not resolve a basedOn pointing
    /// at a style the file does not define, so this has to exist even though nothing names it.
    /// </summary>
    private static Style TableNormalStyle() => new(
        new StyleName { Val = "Normal Table" },
        new UIPriority { Val = 99 },
        new SemiHidden(),
        new UnhideWhenUsed())
    {
        Type = StyleValues.Table,
        StyleId = StyleIds.TableNormal,
        Default = true,
    };

    private static Style NoListStyle() => new(
        new StyleName { Val = "No List" },
        new UIPriority { Val = 99 },
        new SemiHidden(),
        new UnhideWhenUsed())
    {
        Type = StyleValues.Numbering,
        StyleId = StyleIds.NoList,
        Default = true,
    };

    private static Style TitleStyle() => new(
        new StyleName { Val = "Title" },
        new BasedOn { Val = StyleIds.Normal },
        new NextParagraphStyle { Val = StyleIds.Normal },
        new UIPriority { Val = 10 },
        new PrimaryStyle(),
        new StyleParagraphProperties(
            new SpacingBetweenLines
            {
                After = "80",
                Line = "240",
                LineRule = LineSpacingRuleValues.Auto,
            },
            new ContextualSpacing()),
        new StyleRunProperties(
            MajorThemeFont(),
            new FontSize { Val = "56" },
            new FontSizeComplexScript { Val = "56" }))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.Title,
    };

    private static Style SubtitleStyle() => new(
        new StyleName { Val = "Subtitle" },
        new BasedOn { Val = StyleIds.Normal },
        new NextParagraphStyle { Val = StyleIds.Normal },
        new UIPriority { Val = 11 },
        new PrimaryStyle(),
        new StyleParagraphProperties(
            new SpacingBetweenLines
            {
                After = "160",
                Line = "278",
                LineRule = LineSpacingRuleValues.Auto,
            }),
        new StyleRunProperties(
            MajorThemeFont(),
            new Color { Val = "595959" },
            new FontSize { Val = "28" },
            new FontSizeComplexScript { Val = "28" }))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.Subtitle,
    };

    /// <summary>
    /// Headings one to six, on Word's own scale.
    ///
    /// The outline level is the load-bearing part and the one with no visible effect: it is
    /// what the navigation pane reads, what a table-of-contents field collects, and what Word
    /// folds when a reader collapses a section. A heading that looks right and carries no
    /// outline level is one the document's own structure cannot see.
    /// </summary>
    private static Style HeadingStyle(int level, HeadingNumbering numbering, int? numberId)
    {
        (int size, string before, string after) = level switch
        {
            1 => (40, "360", "80"),
            2 => (32, "160", "80"),
            3 => (28, "160", "80"),
            4 => (24, "80", "40"),
            5 => (24, "80", "40"),
            _ => (24, "40", "40"),
        };

        var runProperties = new StyleRunProperties(MajorThemeFont());

        // Italic before color, not after. w:rPr is a schema sequence and it runs
        // rFonts, b, i, ... noProof, color, ... sz - so the toggles come first and the
        // color and size follow, which is the opposite of how a stylesheet reads.
        if (level == 4)
        {
            runProperties.AppendChild(new Italic());
        }

        // Heading 1 is darkened and heading 6 lightened against the same accent, which is
        // what Word's own theme does: the six read as one family rather than six identical
        // teals. The literal beside each theme reference is the fallback for a reader that
        // ignores themes, and is the shade Word would compute anyway.
        runProperties.AppendChild(level switch
        {
            1 => new Color
            {
                Val = "2A6068",
                ThemeColor = ThemeColorValues.Accent1,
                ThemeShade = "BF",
            },
            6 => new Color
            {
                Val = "72B0B7",
                ThemeColor = ThemeColorValues.Accent1,
                ThemeTint = "BF",
            },
            _ => new Color
            {
                Val = DocumentAccent.LightRgb,
                ThemeColor = ThemeColorValues.Accent1,
            },
        });

        runProperties.AppendChild(new FontSize { Val = Text(size) });
        runProperties.AppendChild(new FontSizeComplexScript { Val = Text(size) });

        var paragraphProperties = new StyleParagraphProperties(new KeepNext(), new KeepLines());

        // The section number, when the reader has asked for one. It is attached to the style
        // rather than typed in front of the text, which is what makes Word maintain it: insert
        // a section and everything after it renumbers, delete one and the gap closes. A number
        // written as text is right when the file is written and wrong from the first edit.
        //
        // Numbering properties come after the keep flags and before the spacing - w:pPr is a
        // schema sequence, and this is not a place to guess.
        if (numbering != HeadingNumbering.Off && numberId is { } id && level >= (int)numbering)
        {
            paragraphProperties.AppendChild(new NumberingProperties(
                new NumberingLevelReference { Val = level - (int)numbering },
                new NumberingId { Val = id }));
        }

        paragraphProperties.AppendChild(new SpacingBetweenLines
        {
            Before = before,
            After = after,
            Line = "240",
            LineRule = LineSpacingRuleValues.Auto,
        });

        paragraphProperties.AppendChild(new OutlineLevel { Val = level - 1 });

        return new Style(
            new StyleName { Val = $"heading {level}" },
            new BasedOn { Val = StyleIds.Normal },
            new NextParagraphStyle { Val = StyleIds.Normal },
            new UIPriority { Val = 9 },
            new PrimaryStyle(),
            paragraphProperties,
            runProperties)
        {
            Type = StyleValues.Paragraph,
            StyleId = StyleIds.Heading(level),
        };
    }

    private static Style QuoteStyle() => new(
        new StyleName { Val = "Quote" },
        new BasedOn { Val = StyleIds.Normal },
        new NextParagraphStyle { Val = StyleIds.Normal },
        new UIPriority { Val = 29 },
        new PrimaryStyle(),
        new StyleParagraphProperties(
            new ParagraphBorders(
                new LeftBorder
                {
                    Val = BorderValues.Single,
                    Size = 18U,
                    Space = 8U,
                    Color = DocumentAccent.LightRgb,
                    ThemeColor = ThemeColorValues.Accent1,
                }),
            new SpacingBetweenLines { Before = "160", After = "160" },
            new Indentation { Left = "432", Right = "432" }),
        new StyleRunProperties(
            new Italic(),
            new Color { Val = "5D5D5D" }))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.Quote,
    };

    private static Style CaptionStyle() => new(
        new StyleName { Val = "caption" },
        new BasedOn { Val = StyleIds.Normal },
        new NextParagraphStyle { Val = StyleIds.Normal },
        new UIPriority { Val = 35 },
        new UnhideWhenUsed(),
        new PrimaryStyle(),
        new StyleParagraphProperties(
            new SpacingBetweenLines
            {
                Before = "0",
                After = "200",
                Line = "240",
                LineRule = LineSpacingRuleValues.Auto,
            },
            new Justification { Val = JustificationValues.Center }),
        new StyleRunProperties(
            new Italic(),
            new Color { Val = "5D5D5D" },
            new FontSize { Val = "20" },
            new FontSizeComplexScript { Val = "20" }))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.Caption,
    };

    private static Style ListParagraphStyle() => new(
        new StyleName { Val = "List Paragraph" },
        new BasedOn { Val = StyleIds.Normal },
        new UIPriority { Val = 34 },
        new PrimaryStyle(),
        // No indent of its own. Every list paragraph is given one - by the numbering level it
        // points at, or by hand when it points at none - and a second figure here would be a
        // second opinion about the same thing, disagreeing with the first at every level but
        // the outermost.
        new StyleParagraphProperties(
            new ContextualSpacing()))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.ListParagraph,
    };

    private static Style HyperlinkStyle() => new(
        new StyleName { Val = "Hyperlink" },
        new BasedOn { Val = StyleIds.DefaultParagraphFont },
        new UIPriority { Val = 99 },
        new UnhideWhenUsed(),
        new StyleRunProperties(
            new Color { Val = DocumentAccent.LightRgb, ThemeColor = ThemeColorValues.Hyperlink },
            new Underline { Val = UnderlineValues.Single }))
    {
        Type = StyleValues.Character,
        StyleId = StyleIds.Hyperlink,
    };

    /// <summary>
    /// A fenced or indented code block.
    ///
    /// Two details do most of the work. The border carries no "between" edge, so Word
    /// collapses the identical borders of consecutive paragraphs into a single frame and a
    /// ten-line fence gets one box rather than ten stacked ones - which is why each line of a
    /// fence is its own paragraph in this style rather than one paragraph full of breaks. And
    /// NoProof turns the spell checker off for the run, without which Word underlines every
    /// identifier in the file and the document looks broken.
    /// </summary>
    private static Style CodeBlockStyle() => new(
        new StyleName { Val = "Marqora Code Block" },
        new BasedOn { Val = StyleIds.Normal },
        new NextParagraphStyle { Val = StyleIds.Normal },
        new UIPriority { Val = 99 },
        new PrimaryStyle(),
        new StyleParagraphProperties(
            new KeepLines(),
            new ParagraphBorders(
                // Eight points all round. The vertical figure went to twelve - about one line
                // of the code face, which is what the preview leaves, its fences being padded
                // 1em - and came back when that read as too much air: thirty percent off
                // twelve is 8.4, and a border's space is whole points.
                //
                // Worth knowing, if this looks like a change that undid itself: the padding
                // was never what made fences look inconsistent. Two of them back to back were
                // welding into a single box, and the seam written after each fence is what
                // fixed that.
                new TopBorder { Val = BorderValues.Single, Size = 4U, Space = 8U, Color = CodeBorder },
                new LeftBorder { Val = BorderValues.Single, Size = 4U, Space = 8U, Color = CodeBorder },
                new BottomBorder { Val = BorderValues.Single, Size = 4U, Space = 8U, Color = CodeBorder },
                new RightBorder { Val = BorderValues.Single, Size = 4U, Space = 8U, Color = CodeBorder }),
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CodeFill },
            // Spacing above and below the fence, not between its lines. Contextual spacing is
            // what makes that distinction: Word drops the gap when the neighbouring paragraph
            // has the same style, so ten code lines sit tight against each other and the
            // prose after the last one still gets its air. Set to zero here, the next
            // paragraph began immediately under the box.
            new SpacingBetweenLines
            {
                Before = "160",
                After = "160",
                Line = "264",
                LineRule = LineSpacingRuleValues.Auto,
            },
            // Indentation before contextual spacing: w:pPr is a schema sequence and this is
            // the pair that gets written the wrong way round, because the natural order to
            // think of them in is the reverse of the one the schema lists.
            new Indentation { Left = "115", Right = "115" },
            new ContextualSpacing()),
        new StyleRunProperties(
            MonospaceFont(),

            // Not bold, though inline code is. A fence is already marked out by its shading,
            // its border and its face, and a page of bold monospace is heavy to read - the
            // weight earns its place on a word inside a sentence and not on thirty lines.
            new NoProof(),
            new Color { Val = CodeInk },
            new FontSize { Val = Text(CodeHalfPoints) },
            new FontSizeComplexScript { Val = Text(CodeHalfPoints) }))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.CodeBlock,
    };

    /// <summary>
    /// Inline code. Character-level shading gives the tinted pill; a border would be drawn at
    /// full line height and read as a box around the line rather than around the word.
    /// </summary>
    private static Style CodeCharStyle() => new(
        new StyleName { Val = "Marqora Inline Code" },
        new BasedOn { Val = StyleIds.DefaultParagraphFont },
        new UIPriority { Val = 99 },
        new PrimaryStyle(),
        new StyleRunProperties(
            MonospaceFont(),
            new Bold(),
            new NoProof(),
            new Color { Val = CodeInk },
            new FontSize { Val = Text(CodeHalfPoints) },
            new FontSizeComplexScript { Val = Text(CodeHalfPoints) },
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = CodeFill }))
    {
        Type = StyleValues.Character,
        StyleId = StyleIds.CodeChar,
    };

    /// <summary>
    /// What a == highlight == becomes. Shading rather than Word's own highlight, which offers
    /// sixteen fixed colors and none of them is the preview's soft yellow.
    /// </summary>
    private static Style MarkStyle() => new(
        new StyleName { Val = "Marqora Mark" },
        new BasedOn { Val = StyleIds.DefaultParagraphFont },
        new UIPriority { Val = 99 },
        new StyleRunProperties(
            new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = CalloutColors.MarkRgb,
            }))
    {
        Type = StyleValues.Character,
        StyleId = StyleIds.Mark,
    };

    /// <summary>
    /// One of the five callouts: a colored bar down the left edge and a tinted panel behind.
    ///
    /// The bar carries no "between" edge and the spacing is contextual, which together are
    /// what make a callout of four paragraphs look like one panel rather than four stacked
    /// boxes: Word collapses the identical borders of adjacent paragraphs into a single frame.
    ///
    /// The fill is opaque. The preview draws the panel as the bar color at eight or fourteen
    /// percent over whatever is behind it, and Word's shading has no alpha at all, so the same
    /// color is composited onto white in <see cref="CalloutColors"/> and written flat.
    /// </summary>
    private static Style CalloutStyle(CalloutKind kind) => new(
        new StyleName { Val = $"Marqora {CalloutColors.TitleOf(kind)}" },
        new BasedOn { Val = StyleIds.Normal },
        new NextParagraphStyle { Val = StyleIds.Normal },
        new UIPriority { Val = 99 },
        new PrimaryStyle(),
        new StyleParagraphProperties(
            // The top and bottom edges are drawn in the fill color, so they are invisible as
            // lines. They are here to stop the shading. Word fills a shaded paragraph out to
            // its borders - and, where it has none, out through the paragraph's own spacing -
            // so a panel edged only on the left swallowed whatever gap was set beneath it and
            // the prose after a callout began hard against its bottom. A code fence never had
            // the problem because it is bordered on all four sides. Space is the padding
            // between the text and the edge, which is what the shading now fills to.
            new ParagraphBorders(
                new TopBorder
                {
                    Val = BorderValues.Single,
                    Size = 2U,
                    Space = 6U,
                    Color = CalloutColors.FillOf(kind),
                },
                new LeftBorder
                {
                    Val = BorderValues.Single,
                    Size = 18U,
                    Space = 8U,
                    Color = CalloutColors.BarOf(kind),
                },
                new BottomBorder
                {
                    Val = BorderValues.Single,
                    Size = 2U,
                    Space = 6U,
                    Color = CalloutColors.FillOf(kind),
                }),
            new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = CalloutColors.FillOf(kind),
            },
            // Air below the panel and none between its own paragraphs, which is the same
            // distinction a code fence needs and the same thing that draws it: the contextual
            // spacing below applies the gap only where the next paragraph is something else.
            new SpacingBetweenLines { Before = "0", After = "160" },
            new Indentation { Left = "187", Right = "144" },
            new ContextualSpacing()))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.Callout(kind),
    };

    /// <summary>
    /// The label a callout opens with - "Note", "Warning" - in the callout's own color.
    ///
    /// Markdig draws an icon beside it in the preview, as an inline SVG. Word has nowhere
    /// sensible to put one: it would need an image part per callout and would not follow the
    /// text if the reader restyled the document. The word on its own carries the meaning.
    /// </summary>
    private static Style CalloutTitleStyle(CalloutKind kind) => new(
        new StyleName { Val = $"Marqora {CalloutColors.TitleOf(kind)} Title" },
        new BasedOn { Val = StyleIds.Callout(kind) },
        new NextParagraphStyle { Val = StyleIds.Callout(kind) },
        new UIPriority { Val = 99 },
        new StyleParagraphProperties(
            new KeepNext(),

            // Nothing above the label. The gap over the panel is the previous paragraph's to
            // give, and the padding inside it now comes from the top border's space - before
            // that border existed this had to be 120, because spacing was the only thing the
            // shading would fill.
            new SpacingBetweenLines { Before = "0", After = "0" }),
        new StyleRunProperties(
            new Bold(),
            new Color { Val = CalloutColors.BarOf(kind) }))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.CalloutTitle(kind),
    };

    /// <summary>
    /// The running header or footer: small, and with none of the paragraph spacing body text
    /// carries, so a one-line header sits on its own line rather than pushing itself about.
    /// </summary>
    private static Style RunningStyle(string styleId, string builtInName) => new(
        new StyleName { Val = builtInName },
        new BasedOn { Val = StyleIds.Normal },
        new UIPriority { Val = 99 },
        new UnhideWhenUsed(),
        new StyleParagraphProperties(
            new SpacingBetweenLines
            {
                After = "0",
                Line = "240",
                LineRule = LineSpacingRuleValues.Auto,
            }),
        new StyleRunProperties(
            new FontSize { Val = "18" },
            new FontSizeComplexScript { Val = "18" }))
    {
        Type = StyleValues.Paragraph,
        StyleId = styleId,
    };

    /// <summary>
    /// One line of the table of contents.
    ///
    /// Defined rather than left to Word, and that is the whole point of it. A contents field
    /// builds its entries in the TOC 1, TOC 2 and TOC 3 styles; when the file does not define
    /// those, the entries end up carrying the formatting of the headings they came from - so a
    /// contents list comes out bold, in the heading face, at heading sizes, each line different
    /// from the last. These are ordinary body text with an indent per level, which is what a
    /// table of contents looks like in Word and what a reader expects.
    ///
    /// No color and no underline either. The field is written with the hyperlink switch, so
    /// every entry is clickable, and Word draws those in the Hyperlink style unless the TOC
    /// styles say otherwise - which would give a contents page of teal underlined text.
    /// </summary>
    /// <summary>
    /// The heading above the contents field.
    ///
    /// Word's own style, and it exists for exactly one reason: it carries no outline level. A
    /// contents field lists every paragraph inside the outline range it was given, so a
    /// "Contents" heading styled as Heading 1 sat inside that range and the contents listed
    /// itself - first line, pointing at the page it was already on.
    ///
    /// Based on Heading 1 so that it looks like one, with the outline level taken back to
    /// nine - body text - which keeps it out of the navigation pane as well as out of the
    /// contents.
    ///
    /// Numbering has to be taken back too, but only when Heading 1 has any. A style based on
    /// Heading 1 inherits its numbering instance, so where the reader has asked for sections
    /// numbered from Heading 1 the contents heading would be handed a number of its own -
    /// instance zero is Word's way of saying none. Where numbering starts lower down, or is
    /// off, Heading 1 carries none and there is nothing to suppress: saying so anyway would
    /// put a numbering reference into the styles of every document that asked for no numbers.
    /// </summary>
    private static Style ContentsHeadingStyle(HeadingNumbering numbering)
    {
        var paragraphProperties = new StyleParagraphProperties();

        if (numbering == HeadingNumbering.FromHeading1)
        {
            paragraphProperties.AppendChild(new NumberingProperties(new NumberingId { Val = 0 }));
        }

        paragraphProperties.AppendChild(new OutlineLevel { Val = 9 });

        return new Style(
            new StyleName { Val = "TOC Heading" },
            new BasedOn { Val = StyleIds.Heading(1) },
            new NextParagraphStyle { Val = StyleIds.Normal },
            new UIPriority { Val = 39 },
            new UnhideWhenUsed(),
            paragraphProperties)
        {
            Type = StyleValues.Paragraph,
            StyleId = StyleIds.TocHeading,
        };
    }

    private static Style ContentsEntryStyle(int level) => new(
        new StyleName { Val = $"toc {level}" },
        new BasedOn { Val = StyleIds.Normal },
        new NextParagraphStyle { Val = StyleIds.Normal },
        new UIPriority { Val = 39 },
        new UnhideWhenUsed(),
        new StyleParagraphProperties(
            new SpacingBetweenLines { After = "0", Line = "278", LineRule = LineSpacingRuleValues.Auto },
            new Indentation { Left = ((level - 1) * 220).ToString(CultureInfo.InvariantCulture) }),
        new StyleRunProperties(
            MinorThemeFont(),
            new Color { Val = "auto" },
            new FontSize { Val = Text(BodyHalfPoints) },
            new FontSizeComplexScript { Val = Text(BodyHalfPoints) }))
    {
        Type = StyleValues.Paragraph,
        StyleId = $"TOC{level.ToString(CultureInfo.InvariantCulture)}",
    };

    /// <summary>
    /// The text of a footnote: smaller than the body, and single spaced, which is what Word's
    /// own footnote style does and what a printed page expects at the foot of it.
    /// </summary>
    private static Style FootnoteTextStyle() => new(
        new StyleName { Val = "footnote text" },
        new BasedOn { Val = StyleIds.Normal },
        new UIPriority { Val = 99 },
        new SemiHidden(),
        new UnhideWhenUsed(),
        new StyleParagraphProperties(
            new SpacingBetweenLines
            {
                After = "0",
                Line = "240",
                LineRule = LineSpacingRuleValues.Auto,
            }),
        new StyleRunProperties(
            new FontSize { Val = "20" },
            new FontSizeComplexScript { Val = "20" }))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.FootnoteText,
    };

    /// <summary>The small raised number, both in the text and in front of the note.</summary>
    private static Style FootnoteReferenceStyle() => new(
        new StyleName { Val = "footnote reference" },
        new BasedOn { Val = StyleIds.DefaultParagraphFont },
        new UIPriority { Val = 99 },
        new SemiHidden(),
        new UnhideWhenUsed(),
        new StyleRunProperties(
            new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }))
    {
        Type = StyleValues.Character,
        StyleId = StyleIds.FootnoteReference,
    };

    /// <summary>
    /// A definition list's term. Word has no such construct, so the shape is carried by two
    /// ordinary paragraph styles: a bold term, and its definition indented beneath it.
    /// </summary>
    private static Style DefinitionTermStyle() => new(
        new StyleName { Val = "Marqora Term" },
        new BasedOn { Val = StyleIds.Normal },
        new NextParagraphStyle { Val = StyleIds.DefinitionItem },
        new UIPriority { Val = 99 },
        new PrimaryStyle(),
        new StyleParagraphProperties(
            new KeepNext(),
            new SpacingBetweenLines { Before = "160", After = "0" }),
        new StyleRunProperties(new Bold()))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.DefinitionTerm,
    };

    private static Style DefinitionItemStyle() => new(
        new StyleName { Val = "Marqora Definition" },
        new BasedOn { Val = StyleIds.Normal },
        new NextParagraphStyle { Val = StyleIds.Normal },
        new UIPriority { Val = 99 },
        new PrimaryStyle(),
        new StyleParagraphProperties(
            new SpacingBetweenLines { Before = "0", After = "80" },
            new Indentation { Left = "432" }))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.DefinitionItem,
    };

    /// <summary>
    /// The table style, and the one accented thing on the page.
    ///
    /// The header fill is a theme reference with a literal beside it: the reference is what
    /// lets the Design tab restyle the table along with everything else, and the literal is
    /// what a reader that ignores themes falls back to. Only the first row is conditional -
    /// banded rows are deliberately not switched on, because the preview does not band either
    /// and a striped table reads as a different kind of document.
    /// </summary>
    private static Style TableStyle() => new(
        new StyleName { Val = "Marqora Table" },
        new BasedOn { Val = StyleIds.TableNormal },
        new UIPriority { Val = 59 },
        new StyleParagraphProperties(
            new SpacingBetweenLines
            {
                Before = "40",
                After = "40",
                Line = "240",
                LineRule = LineSpacingRuleValues.Auto,
            }),
        new StyleTableProperties(
            new TableStyleRowBandSize { Val = 1 },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = CodeBorder },
                new LeftBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = CodeBorder },
                new BottomBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = CodeBorder },
                new RightBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = CodeBorder },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = CodeBorder },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = CodeBorder }),
            new TableCellMarginDefault(
                new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new TableCellLeftMargin { Width = 120, Type = TableWidthValues.Dxa },
                new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                new TableCellRightMargin { Width = 120, Type = TableWidthValues.Dxa })),
        new TableStyleProperties(
            new StyleParagraphProperties(new KeepNext()),
            new StyleRunProperties(
                new Bold(),
                new Color { Val = "FFFFFF", ThemeColor = ThemeColorValues.Background1 }),
            new TableStyleConditionalFormattingTableCellProperties(
                new Shading
                {
                    Val = ShadingPatternValues.Clear,
                    Color = "auto",
                    Fill = DocumentAccent.LightRgb,
                    ThemeFill = ThemeColorValues.Accent1,
                }))
        {
            Type = TableStyleOverrideValues.FirstRow,
        })
    {
        Type = StyleValues.Table,
        StyleId = StyleIds.Table,
    };

    private static RunFonts MajorThemeFont() => new()
    {
        AsciiTheme = ThemeFontValues.MajorHighAnsi,
        HighAnsiTheme = ThemeFontValues.MajorHighAnsi,
        ComplexScriptTheme = ThemeFontValues.MajorBidi,
        EastAsiaTheme = ThemeFontValues.MajorEastAsia,
    };

    private static RunFonts MinorThemeFont() => new()
    {
        AsciiTheme = ThemeFontValues.MinorHighAnsi,
        HighAnsiTheme = ThemeFontValues.MinorHighAnsi,
        ComplexScriptTheme = ThemeFontValues.MinorBidi,
        EastAsiaTheme = ThemeFontValues.MinorEastAsia,
    };

    private static RunFonts MonospaceFont() => new()
    {
        Ascii = CodeFont,
        HighAnsi = CodeFont,
        ComplexScript = CodeFont,
    };

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
