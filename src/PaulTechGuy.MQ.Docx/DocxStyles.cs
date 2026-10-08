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
/// fonts are theme references, because that is what lets the Design tab restyle the whole
/// document.
///
/// Colors are the color theme's, as plain values. A theme has far more of them than Word's
/// scheme has slots - six heading colors, a quote ink, a table stripe - and in OOXML a theme
/// reference outranks the literal written beside it, so a reference would quietly put the
/// scheme's color back over the theme's. The one exception is the hyperlink, whose scheme
/// slot DocxTheme sets to the theme's link color, so the two cannot disagree.
///
/// Type and space are the paper spec's (<see cref="PaperSpec"/>), the same values the print
/// stylesheet reads, so the .docx and the PDF of one document are set alike. Every size, line
/// height, gap and face below comes from there; nothing here states one of its own. Faces are
/// reached by role - text and display through the document theme, which DocxTheme sets from
/// <see cref="PaperFaces"/>, and code by name from the same place.
/// </summary>
internal static class DocxStyles
{
    /// <summary>
    /// How far a quote stands in from each margin, 0.3in. A nested quote stands in this much
    /// again per level (BlockRenderer.WriteQuote), so the style and the nesting agree.
    /// </summary>
    public const int QuoteIndentTwips = 432;

    /// <param name="colors">
    /// The color theme's light palette. Every color a style carries comes from it, so the
    /// exported document wears the theme the preview does.
    /// </param>
    public static void Write(
        StyleDefinitionsPart part,
        DocxColors colors,
        HeadingNumbering headingNumbering = HeadingNumbering.Off,
        int? headingNumberId = null)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(colors);

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
            styles.AppendChild(HeadingStyle(level, headingNumbering, headingNumberId, colors));
        }

        styles.AppendChild(QuoteStyle(colors));
        styles.AppendChild(CaptionStyle());
        styles.AppendChild(ListParagraphStyle());
        styles.AppendChild(HyperlinkStyle(colors));
        styles.AppendChild(CodeBlockStyle(colors));
        styles.AppendChild(CodeCharStyle(colors));
        styles.AppendChild(MarkStyle(colors));
        styles.AppendChild(TableStyle(colors));
        styles.AppendChild(DefinitionTermStyle());
        styles.AppendChild(DefinitionItemStyle());
        styles.AppendChild(FootnoteTextStyle());
        styles.AppendChild(FootnoteReferenceStyle(colors));
        styles.AppendChild(RunningStyle(StyleIds.Header, "header"));
        styles.AppendChild(RunningStyle(StyleIds.Footer, "footer"));

        for (int level = 1; level <= 3; level++)
        {
            styles.AppendChild(ContentsEntryStyle(level));
        }

        styles.AppendChild(ContentsHeadingStyle(headingNumbering));

        foreach (CalloutKind kind in Enum.GetValues<CalloutKind>())
        {
            styles.AppendChild(CalloutStyle(kind, colors));
            styles.AppendChild(CalloutTitleStyle(kind, colors));
        }

        part.Styles = styles;
    }

    /// <summary>
    /// What every paragraph and run starts from before any style applies: the paper spec's
    /// body text. The font is a theme reference so that swapping the theme swaps the document.
    /// </summary>
    private static DocDefaults BuildDocDefaults() => new(
        new RunPropertiesDefault(
            new RunPropertiesBaseStyle(
                MinorThemeFont(),
                SizeOf(PaperSpec.Body),
                ComplexSizeOf(PaperSpec.Body),
                new Languages { Val = "en-US" })),
        new ParagraphPropertiesDefault(
            new ParagraphPropertiesBaseStyle(SpacingOf(PaperSpec.Body))));

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
            SpacingOf(PaperSpec.CoverTitle),
            new ContextualSpacing()),
        new StyleRunProperties(
            FontOf(PaperSpec.CoverTitle),
            SizeOf(PaperSpec.CoverTitle),
            ComplexSizeOf(PaperSpec.CoverTitle)))
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
        new StyleParagraphProperties(SpacingOf(PaperSpec.CoverSubtitle)),
        new StyleRunProperties(
            FontOf(PaperSpec.CoverSubtitle),
            new Color { Val = "595959" },
            SizeOf(PaperSpec.CoverSubtitle),
            ComplexSizeOf(PaperSpec.CoverSubtitle)))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.Subtitle,
    };

    /// <summary>
    /// Headings one to six, on the paper spec's scale - the preview's own, so a heading is the
    /// same size in the PDF and the .docx - in the display face, which is semibold.
    ///
    /// The outline level is the load-bearing part and the one with no visible effect: it is
    /// what the navigation pane reads, what a table-of-contents field collects, and what Word
    /// folds when a reader collapses a section. A heading that looks right and carries no
    /// outline level is one the document's own structure cannot see.
    /// </summary>
    private static Style HeadingStyle(int level, HeadingNumbering numbering, int? numberId, DocxColors colors)
    {
        PaperElement element = PaperSpec.Heading(level);

        var runProperties = new StyleRunProperties(FontOf(element));

        // The toggles before color, not after. w:rPr is a schema sequence and it runs
        // rFonts, b, i, caps, ... noProof, color, ... sz - so the toggles come first and the
        // color and size follow, which is the opposite of how a stylesheet reads.
        if (element.Italic)
        {
            runProperties.AppendChild(new Italic());
        }

        if (element.Uppercase)
        {
            runProperties.AppendChild(new Caps());
        }

        // The color theme's own color for the level, written as a plain color rather than a
        // reference into the document theme. A reference would win over the literal - in
        // OOXML themeColor outranks val - and the theme has six heading colors where Word's
        // scheme has no heading slot at all; a shade of accent 1 was the old answer, from
        // before a heading had a color of its own.
        runProperties.AppendChild(new Color { Val = colors.Heading(level) });

        runProperties.AppendChild(SizeOf(element));
        runProperties.AppendChild(ComplexSizeOf(element));

        var paragraphProperties = new StyleParagraphProperties(new KeepNext(), new KeepLines());

        // The line under a level 2 heading, which the preview draws. Borders come before the
        // numbering in w:pPr - the schema runs keepNext, keepLines, ..., numPr, ..., pBdr - so
        // it is held back and placed after the numbering below.
        ParagraphBorders? rule = level == 2
            ? new ParagraphBorders(new BottomBorder
            {
                Val = BorderValues.Single,
                Size = 4U,
                Space = 4U,
                Color = colors.HeadingRule,
            })
            : null;

        // The section number, when the reader has asked for one. It is attached to the style
        // rather than typed in front of the text, which is what makes Word maintain it: insert
        // a section and everything after it renumbers, delete one and the gap closes. A number
        // written as text is right when the file is written and wrong from the first edit.
        //
        // Every heading level is linked, the ones above the start included: they print no
        // number, but they are what restarts the count beneath them - see PlanHeadings. Level
        // n of the list is heading n + 1 whatever the start.
        //
        // Numbering properties come after the keep flags and before the spacing - w:pPr is a
        // schema sequence, and this is not a place to guess.
        if (numbering != HeadingNumbering.Off && numberId is { } id)
        {
            paragraphProperties.AppendChild(new NumberingProperties(
                new NumberingLevelReference { Val = level - 1 },
                new NumberingId { Val = id }));
        }

        if (rule is not null)
        {
            paragraphProperties.AppendChild(rule);
        }

        paragraphProperties.AppendChild(SpacingOf(element));

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

    /// <summary>
    /// A blockquote: the theme's bar down the left, its quote ink, and its fill behind when
    /// the theme gives one - a fill equal to the page is left out rather than painted white.
    /// </summary>
    private static Style QuoteStyle(DocxColors colors)
    {
        var paragraph = new StyleParagraphProperties(
            new ParagraphBorders(
                new LeftBorder
                {
                    Val = BorderValues.Single,
                    Size = 18U,
                    Space = 8U,
                    Color = colors.QuoteBar,
                }));

        // Shading after the borders and before the spacing: w:pPr runs pBdr, shd, ..., spacing.
        if (colors.QuoteFill is { } fill)
        {
            paragraph.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill });
        }

        paragraph.AppendChild(SpacingOf(PaperSpec.Quote));
        paragraph.AppendChild(new Indentation
        {
            Left = QuoteIndentTwips.ToString(CultureInfo.InvariantCulture),
            Right = QuoteIndentTwips.ToString(CultureInfo.InvariantCulture),
        });

        // Upright, as the preview sets a quote: the bar, the fill and the ink already mark it
        // out, and the paper spec settled on upright for both exports.
        var run = new StyleRunProperties();

        if (PaperSpec.Quote.Italic)
        {
            run.AppendChild(new Italic());
        }

        run.AppendChild(new Color { Val = colors.QuoteText });
        run.AppendChild(SizeOf(PaperSpec.Quote));
        run.AppendChild(ComplexSizeOf(PaperSpec.Quote));

        return new Style(
            new StyleName { Val = "Quote" },
            new BasedOn { Val = StyleIds.Normal },
            new NextParagraphStyle { Val = StyleIds.Normal },
            new UIPriority { Val = 29 },
            new PrimaryStyle(),
            paragraph,
            run)
        {
            Type = StyleValues.Paragraph,
            StyleId = StyleIds.Quote,
        };
    }

    private static Style CaptionStyle() => new(
        new StyleName { Val = "caption" },
        new BasedOn { Val = StyleIds.Normal },
        new NextParagraphStyle { Val = StyleIds.Normal },
        new UIPriority { Val = 35 },
        new UnhideWhenUsed(),
        new PrimaryStyle(),
        new StyleParagraphProperties(
            SpacingOf(PaperSpec.Caption),
            new Justification { Val = JustificationValues.Center }),
        new StyleRunProperties(
            new Italic(),
            new Color { Val = "5D5D5D" },
            SizeOf(PaperSpec.Caption),
            ComplexSizeOf(PaperSpec.Caption)))
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

    /// <summary>
    /// A link, in the theme's link color. Still a reference to the document theme's hyperlink
    /// slot, because the theme part sets that slot to the same color - see DocxTheme - so
    /// Word's Design tab can restyle links along with everything else.
    /// </summary>
    private static Style HyperlinkStyle(DocxColors colors) => new(
        new StyleName { Val = "Hyperlink" },
        new BasedOn { Val = StyleIds.DefaultParagraphFont },
        new UIPriority { Val = 99 },
        new UnhideWhenUsed(),
        new StyleRunProperties(
            new Color { Val = colors.Link, ThemeColor = ThemeColorValues.Hyperlink },
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
    private static Style CodeBlockStyle(DocxColors colors) => new(
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
                new TopBorder { Val = BorderValues.Single, Size = 4U, Space = 8U, Color = colors.CodeBlockBorder },
                new LeftBorder { Val = BorderValues.Single, Size = 4U, Space = 8U, Color = colors.CodeBlockBorder },
                new BottomBorder { Val = BorderValues.Single, Size = 4U, Space = 8U, Color = colors.CodeBlockBorder },
                new RightBorder { Val = BorderValues.Single, Size = 4U, Space = 8U, Color = colors.CodeBlockBorder }),
            new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = colors.CodeBlockFill },
            // Spacing above and below the fence, not between its lines. Contextual spacing is
            // what makes that distinction: Word drops the gap when the neighbouring paragraph
            // has the same style, so ten code lines sit tight against each other and the
            // prose after the last one still gets its air. Set to zero here, the next
            // paragraph began immediately under the box.
            SpacingOf(PaperSpec.CodeBlock),
            // Indentation before contextual spacing: w:pPr is a schema sequence and this is
            // the pair that gets written the wrong way round, because the natural order to
            // think of them in is the reverse of the one the schema lists.
            new Indentation { Left = "115", Right = "115" },
            new ContextualSpacing()),
        new StyleRunProperties(
            FontOf(PaperSpec.CodeBlock),

            // Not bold. A fence is already marked out by its shading, its border and its
            // face, and a page of bold monospace is heavy to read.
            new NoProof(),
            new Color { Val = colors.CodeBlockText },
            SizeOf(PaperSpec.CodeBlock),
            ComplexSizeOf(PaperSpec.CodeBlock)))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.CodeBlock,
    };

    /// <summary>
    /// Inline code. Character-level shading gives the tinted pill; a border would be drawn at
    /// full line height and read as a box around the line rather than around the word.
    ///
    /// Regular weight, as the preview and the PDF set it. It was bold here once, which made
    /// every code span in a Word export heavier than the same span on paper from the PDF.
    /// </summary>
    private static Style CodeCharStyle(DocxColors colors)
    {
        var run = new StyleRunProperties(FontOf(PaperSpec.CodeInline));

        if (PaperSpec.CodeInline.Bold)
        {
            run.AppendChild(new Bold());
        }

        run.AppendChild(new NoProof());
        run.AppendChild(new Color { Val = colors.CodeInlineText });
        run.AppendChild(SizeOf(PaperSpec.CodeInline));
        run.AppendChild(ComplexSizeOf(PaperSpec.CodeInline));
        run.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = colors.CodeInlineFill });

        return new Style(
            new StyleName { Val = "Marqora Inline Code" },
            new BasedOn { Val = StyleIds.DefaultParagraphFont },
            new UIPriority { Val = 99 },
            new PrimaryStyle(),
            run)
        {
            Type = StyleValues.Character,
            StyleId = StyleIds.CodeChar,
        };
    }

    /// <summary>
    /// What a == highlight == becomes. Shading rather than Word's own highlight, which offers
    /// sixteen fixed colors and none of them is the preview's soft yellow.
    /// </summary>
    private static Style MarkStyle(DocxColors colors) => new(
        new StyleName { Val = "Marqora Mark" },
        new BasedOn { Val = StyleIds.DefaultParagraphFont },
        new UIPriority { Val = 99 },
        new StyleRunProperties(
            new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = colors.MarkFill,
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
    /// The bar, the fill and the title are the color theme's three slots for the kind. The fill
    /// is opaque in the theme file already - Word's shading has no alpha at all, which is one of
    /// the reasons every theme color is written that way - so it goes in as it stands.
    /// </summary>
    private static Style CalloutStyle(CalloutKind kind, DocxColors colors) => new(
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
                    Color = colors.CalloutFill(kind),
                },
                new LeftBorder
                {
                    Val = BorderValues.Single,
                    Size = 18U,
                    Space = 8U,
                    Color = colors.CalloutBar(kind),
                },
                new BottomBorder
                {
                    Val = BorderValues.Single,
                    Size = 2U,
                    Space = 6U,
                    Color = colors.CalloutFill(kind),
                }),
            new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = colors.CalloutFill(kind),
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
    private static Style CalloutTitleStyle(CalloutKind kind, DocxColors colors) => new(
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
            new Color { Val = colors.CalloutTitle(kind) }))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.CalloutTitle(kind),
    };

    /// <summary>
    /// The running header or footer: small, and with none of the paragraph spacing body text
    /// carries, so a one-line header sits on its own line rather than pushing itself about.
    /// </summary>
    private static Style RunningStyle(string styleId, string builtInName)
    {
        PaperElement element = styleId == StyleIds.Header ? PaperSpec.RunningHeader : PaperSpec.RunningFooter;

        return new Style(
            new StyleName { Val = builtInName },
            new BasedOn { Val = StyleIds.Normal },
            new UIPriority { Val = 99 },
            new UnhideWhenUsed(),
            new StyleParagraphProperties(SpacingOf(element)),
            new StyleRunProperties(
                FontOf(element),
                SizeOf(element),
                ComplexSizeOf(element)))
        {
            Type = StyleValues.Paragraph,
            StyleId = styleId,
        };
    }

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
    /// Numbering has to be taken back too, whenever headings are numbered at all. A style based
    /// on Heading 1 inherits its numbering instance, and Heading 1 is in the list in every
    /// numbered mode - as a number from Heading 1, and as the unnumbered level that restarts
    /// the count otherwise. Inheriting it, the contents heading would be handed a number of its
    /// own, or would silently restart the count it sits in front of. Instance zero is Word's way
    /// of saying none. With numbering off, Heading 1 carries none and there is nothing to
    /// suppress: saying so anyway would put a numbering reference into the styles of every
    /// document that asked for no numbers.
    /// </summary>
    private static Style ContentsHeadingStyle(HeadingNumbering numbering)
    {
        var paragraphProperties = new StyleParagraphProperties();

        if (numbering != HeadingNumbering.Off)
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
            SpacingOf(PaperSpec.ContentsEntry),
            new Indentation { Left = ((level - 1) * 220).ToString(CultureInfo.InvariantCulture) }),
        new StyleRunProperties(
            FontOf(PaperSpec.ContentsEntry),
            new Color { Val = "auto" },
            SizeOf(PaperSpec.ContentsEntry),
            ComplexSizeOf(PaperSpec.ContentsEntry)))
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
        new StyleParagraphProperties(SpacingOf(PaperSpec.Footnote)),
        new StyleRunProperties(
            SizeOf(PaperSpec.Footnote),
            ComplexSizeOf(PaperSpec.Footnote)))
    {
        Type = StyleValues.Paragraph,
        StyleId = StyleIds.FootnoteText,
    };

    /// <summary>The small raised number, both in the text and in front of the note.</summary>
    private static Style FootnoteReferenceStyle(DocxColors colors) => new(
        new StyleName { Val = "footnote reference" },
        new BasedOn { Val = StyleIds.DefaultParagraphFont },
        new UIPriority { Val = 99 },
        new SemiHidden(),
        new UnhideWhenUsed(),
        new StyleRunProperties(
            new Color { Val = colors.FootnoteReference },
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
    /// The table style: the color theme's borders, its header fill, ink and the heavier rule
    /// under the header, and its stripe behind every other body row - the table the preview
    /// draws.
    ///
    /// Plain colors rather than references into the document theme. A reference outranks the
    /// literal beside it, and the theme has a header fill of its own where the old style
    /// borrowed accent 1. The banding is the preview's own: body rows two, four and on, which
    /// is band 2 once the header row is set apart - and when the theme's stripe is the page
    /// itself there is no band to define.
    /// </summary>
    private static Style TableStyle(DocxColors colors)
    {
        string border = colors.TableBorder;

        var style = new Style(
            new StyleName { Val = "Marqora Table" },
            new BasedOn { Val = StyleIds.TableNormal },
            new UIPriority { Val = 59 },
            new StyleParagraphProperties(SpacingOf(PaperSpec.Table)),
            new StyleRunProperties(
                SizeOf(PaperSpec.Table),
                ComplexSizeOf(PaperSpec.Table)),
            new StyleTableProperties(
                new TableStyleRowBandSize { Val = 1 },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = border },
                    new LeftBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = border },
                    new BottomBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = border },
                    new RightBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = border },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = border },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U, Space = 0U, Color = border }),
                new TableCellMarginDefault(
                    new TopMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                    new TableCellLeftMargin { Width = 120, Type = TableWidthValues.Dxa },
                    new BottomMargin { Width = "60", Type = TableWidthUnitValues.Dxa },
                    new TableCellRightMargin { Width = 120, Type = TableWidthValues.Dxa })),
            new TableStyleProperties(
                new StyleParagraphProperties(new KeepNext()),
                new StyleRunProperties(
                    new Bold(),
                    new Color { Val = colors.TableHeaderText }),

                // Borders before shading: w:tcPr runs tcBorders, shd. Twelve eighths of a point
                // is the preview's two pixels.
                new TableStyleConditionalFormattingTableCellProperties(
                    new TableCellBorders(
                        new BottomBorder
                        {
                            Val = BorderValues.Single,
                            Size = 12U,
                            Space = 0U,
                            Color = colors.TableHeaderRule,
                        }),
                    new Shading
                    {
                        Val = ShadingPatternValues.Clear,
                        Color = "auto",
                        Fill = colors.TableHeaderFill,
                    }))
            {
                Type = TableStyleOverrideValues.FirstRow,
            })
        {
            Type = StyleValues.Table,
            StyleId = StyleIds.Table,
        };

        if (colors.TableStripe is { } stripe)
        {
            style.AppendChild(new TableStyleProperties(
                new TableStyleConditionalFormattingTableCellProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = stripe }))
            {
                Type = TableStyleOverrideValues.Band2Horizontal,
            });
        }

        return style;
    }

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
        Ascii = PaperFaces.Code.WordFamily,
        HighAnsi = PaperFaces.Code.WordFamily,
        ComplexScript = PaperFaces.Code.WordFamily,
    };

    /// <summary>The face an element's role names: the theme's two faces, or code by name.</summary>
    private static RunFonts FontOf(PaperElement element) => element.Face switch
    {
        PaperFaceRole.Display => MajorThemeFont(),
        PaperFaceRole.Code => MonospaceFont(),
        _ => MinorThemeFont(),
    };

    /// <summary>An element's size, in the half-points Word stores.</summary>
    private static string HalfPoints(PaperElement element) => Text((int)Math.Round(element.SizePoints * 2));

    private static FontSize SizeOf(PaperElement element) => new() { Val = HalfPoints(element) };

    private static FontSizeComplexScript ComplexSizeOf(PaperElement element) => new() { Val = HalfPoints(element) };

    /// <summary>
    /// An element's line height and the space around it. Points become twips, twenty to the
    /// point; a line height of 1.4 is Word's "auto" spacing of 336, 240 being single.
    /// </summary>
    private static SpacingBetweenLines SpacingOf(PaperElement element) => new()
    {
        Before = Twips(element.BeforePoints),
        After = Twips(element.AfterPoints),
        Line = Text((int)Math.Round(element.LineHeight * 240)),
        LineRule = LineSpacingRuleValues.Auto,
    };

    private static string Twips(double points) => Text((int)Math.Round(points * 20));

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}
