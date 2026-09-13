// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// The formatting a run has accumulated on the way down the inline tree.
///
/// Markdown nests emphasis, and Word does not: <c>**bold _and italic_**</c> is two levels of
/// tree and one run wearing both. So the walker carries this down rather than emitting as it
/// goes, and each level returns a copy with one more thing set. A struct because it is copied
/// at every level of every inline in the document and never outlives the call.
/// </summary>
internal readonly record struct RunFormat
{
    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Strike { get; init; }

    public bool Underline { get; init; }

    /// <summary>Subscript and superscript are one Word property, so they cannot both be on.</summary>
    public VerticalPositionValues? VerticalAlignment { get; init; }

    /// <summary>
    /// A character style, for the two inline things Word has no toggle for: code and mark.
    /// Only one can apply, which is the same limit Word's own UI has.
    /// </summary>
    public string? CharacterStyle { get; init; }

    /// <summary>Set when the run sits inside a link, so it can take the Hyperlink style.</summary>
    public bool Hyperlink { get; init; }

    /// <summary>Ink, as six hex digits, when inline HTML asked for one.</summary>
    public string? Color { get; init; }

    /// <summary>What is behind the text, as six hex digits.</summary>
    public string? Shading { get; init; }

    /// <summary>
    /// A run that wears nothing, whatever the markdown around it asked for.
    ///
    /// Headings are written this way, and the reason is the table of contents. Word does not
    /// build a contents entry out of the heading's text alone - it copies the heading's
    /// <b>direct</b> character formatting along with it, and direct formatting beats the TOC
    /// style that sets the face and the size of the line. So a heading holding
    /// <c>`--check`</c> or <c>**bold**</c> puts a bold shaded monospaced word in the middle of
    /// an otherwise ordinary contents line, and there is no field switch that turns it off.
    /// The only place to stop it is here, where the heading's runs are written.
    ///
    /// The alternative was a TC field per heading, holding the text the contents should show.
    /// That text is a copy: right when the file is written and wrong from the first time
    /// somebody renames a heading in Word - the same reason the heading numbers are Word's own
    /// rather than written out - and TC entries lose those numbers as well.
    ///
    /// <see cref="Build"/> is the gate. Everything else is still carried down the inline tree
    /// as it always was; none of it reaches the XML.
    /// </summary>
    public bool Plain { get; init; }

    public RunFormat WithBold() => this with { Bold = true };

    public RunFormat WithItalic() => this with { Italic = true };

    public RunFormat WithStrike() => this with { Strike = true };

    public RunFormat WithUnderline() => this with { Underline = true };

    public RunFormat WithSubscript() =>
        this with { VerticalAlignment = VerticalPositionValues.Subscript };

    public RunFormat WithSuperscript() =>
        this with { VerticalAlignment = VerticalPositionValues.Superscript };

    public RunFormat WithCharacterStyle(string styleId) =>
        this with { CharacterStyle = styleId };

    public RunFormat WithHyperlink() => this with { Hyperlink = true };

    public RunFormat WithColor(string rgb) => this with { Color = rgb };

    public RunFormat WithShading(string rgb) => this with { Shading = rgb };

    /// <summary>
    /// The properties element, or null when nothing is set and the run needs none.
    ///
    /// The order of the children is not a preference. A run's properties are a schema
    /// sequence - rStyle, then the toggles, then color and size, then underline, and vertical
    /// alignment near the end - and Word answers a wrong order by offering to repair the
    /// file rather than by saying what is wrong. This method is the only place that order is
    /// decided, so there is one place to get it right.
    /// </summary>
    public RunProperties? Build()
    {
        // A plain run refuses the lot, and refuses it here rather than at each With - see
        // Plain for what a heading's formatting does to the contents page.
        if (Plain || this == default)
        {
            return null;
        }

        var properties = new RunProperties();

        // A link inside inline code takes the code style; the link color would fight the
        // code color and code is the stronger signal about what the text is.
        string? styleId = CharacterStyle ?? (Hyperlink ? StyleIds.Hyperlink : null);

        if (styleId is not null)
        {
            properties.AppendChild(new RunStyle { Val = styleId });
        }

        if (Bold)
        {
            properties.AppendChild(new Bold());
        }

        if (Italic)
        {
            properties.AppendChild(new Italic());
        }

        if (Strike)
        {
            properties.AppendChild(new Strike());
        }

        // Color before size, and both after the toggles: w:rPr runs rStyle, b, i, strike,
        // color, sz, u, shd, vertAlign, and Word repairs a file that says otherwise.
        if (Color is { Length: > 0 } ink)
        {
            properties.AppendChild(new Color { Val = ink });
        }

        if (Underline)
        {
            properties.AppendChild(new Underline { Val = UnderlineValues.Single });
        }

        if (Shading is { Length: > 0 } fill)
        {
            properties.AppendChild(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = fill,
            });
        }

        if (VerticalAlignment is { } alignment)
        {
            properties.AppendChild(new VerticalTextAlignment { Val = alignment });
        }

        return properties;
    }

    /// <summary>
    /// A run of text carrying this formatting.
    ///
    /// The space-preserving flag is set unconditionally and is not optional: without it a run
    /// holding " and " loses both spaces, because XML collapses leading and trailing
    /// whitespace unless told not to. Markdown produces such runs constantly - every
    /// <c>a **b** c</c> has one - so the words would run together throughout the document.
    /// </summary>
    public Run ToRun(string text)
    {
        var run = new Run();

        if (Build() is { } properties)
        {
            run.AppendChild(properties);
        }

        run.AppendChild(new Text(XmlSafeText.Clean(text))
        {
            Space = SpaceProcessingModeValues.Preserve,
        });

        return run;
    }
}
