// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// The style identifiers a paragraph or run can name.
///
/// Word's own styles have fixed ids - "Heading1", "Quote", "ListParagraph" - and using them
/// rather than inventing names is what makes the exported file behave like a Word document:
/// the Styles gallery shows them where a user expects, the navigation pane finds the
/// headings, and a different style set from the Design tab restyles the lot.
///
/// A style referenced but never defined is applied as Normal, silently, so everything named
/// here has to exist in <see cref="DocxStyles"/>.
/// </summary>
internal static class StyleIds
{
    public const string Normal = "Normal";
    public const string Title = "Title";
    public const string Subtitle = "Subtitle";
    public const string Quote = "Quote";
    public const string IntenseQuote = "IntenseQuote";
    public const string Caption = "Caption";
    public const string ListParagraph = "ListParagraph";
    public const string Hyperlink = "Hyperlink";
    public const string FootnoteText = "FootnoteText";
    public const string FootnoteReference = "FootnoteReference";
    public const string Header = "Header";
    public const string Footer = "Footer";
    public const string DefaultParagraphFont = "DefaultParagraphFont";
    public const string TableNormal = "TableNormal";
    public const string NoList = "NoList";

    /// <summary>Word numbers its heading styles from one; markdown numbers its levels the same.</summary>
    public static string Heading(int level) => $"Heading{Math.Clamp(level, 1, 6)}";

    /// <summary>
    /// The heading above the contents field. Word's own style, and it earns its place by
    /// having no outline level - a "Contents" styled as Heading 1 is inside the range the
    /// field lists, so the contents listed itself on its own first line.
    /// </summary>
    public const string TocHeading = "TOCHeading";

    // ---- Marqora's own, for the things Word has no equivalent of ----

    /// <summary>A fenced or indented code block: shaded, bordered, monospaced.</summary>
    public const string CodeBlock = "MarqoraCode";

    /// <summary>Inline code, as a character style so it can sit inside a sentence.</summary>
    public const string CodeChar = "MarqoraCodeChar";

    /// <summary>What == highlight == becomes.</summary>
    public const string Mark = "MarqoraMark";

    /// <summary>Pipe and grid tables, with the accent-filled header row.</summary>
    public const string Table = "MarqoraTable";

    /// <summary>One of the five GitHub callouts: a colored bar and a tinted panel.</summary>
    public static string Callout(CalloutKind kind) => $"MarqoraCallout{kind}";

    /// <summary>The bold label at the top of a callout, in that callout's own color.</summary>
    public static string CalloutTitle(CalloutKind kind) => $"MarqoraCallout{kind}Title";

    /// <summary>A term in a definition list, which Word has no construct for.</summary>
    public const string DefinitionTerm = "MarqoraTerm";

    /// <summary>Its definition, indented under it.</summary>
    public const string DefinitionItem = "MarqoraDefinition";
}
