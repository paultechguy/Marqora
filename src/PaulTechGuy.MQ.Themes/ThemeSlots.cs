// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Themes;

/// <summary>
/// Every slot a theme colors, and the only place their ids are written down.
///
/// A theme file names each of these once for light and once for dark. The stylesheet reads
/// them as <c>--mq-theme-&lt;id&gt;</c> without declaring them - the host delivers the values
/// as a stylesheet of its own - and the tests check that nothing in the webshell's CSS names a
/// slot that is not here. The authoring document's table is generated from this list, so a new
/// slot reaches the person writing the next theme without anyone remembering to tell them.
///
/// What is deliberately not a slot: the page background and the body text, which stay neutral
/// in every theme; the gray borders of the page furniture; and everything the preview draws to
/// talk to the user rather than to show the document - find hits, review marks, squiggles, the
/// blocked-picture chip. Those keep Marqora's own colors whatever the document wears.
///
/// Each value is an opaque <c>#rrggbb</c>. A tint a theme wants, such as a callout's fill, is
/// written already flattened over its page, because Word cannot express alpha and the rich-text
/// clipboard throws <c>rgba()</c> away; the page is neutral, so the flattened value is exact.
/// </summary>
public static class ThemeSlots
{
    /// <summary>The page behind the document: not a slot, and the same in every theme.</summary>
    public const string Page = "page";

    /// <summary>The document's running text: not a slot, and the same in every theme.</summary>
    public const string BodyText = "body-text";

    public const string HeadingsGroup = "Headings and rules";
    public const string LinksGroup = "Links";
    public const string EmphasisGroup = "Emphasis";
    public const string ListsGroup = "Lists";
    public const string QuotesGroup = "Quotes";
    public const string CalloutsGroup = "Callouts";
    public const string TablesGroup = "Tables";
    public const string CodeGroup = "Code";
    public const string SyntaxGroup = "Syntax";
    public const string HighlightGroup = "Highlight";

    /// <summary>
    /// The one group a theme may leave out. A theme that says <c>"diagrams": "stock"</c> keeps
    /// mermaid's own light and dark themes, which compute dozens of colors from one another
    /// and cannot be reproduced through the handful mermaid's <c>base</c> theme accepts.
    /// Default is that theme: it is what keeps today's diagrams exactly as they are.
    /// </summary>
    public const string DiagramsGroup = "Diagrams";

    private static readonly ContrastRule OnPage = new(Page, Contrast.Text);
    private static readonly ContrastRule LargeOnPage = new(Page, Contrast.LargeText);
    private static readonly ContrastRule OnCode = new("code-block-fill", Contrast.Text);

    public static IReadOnlyList<ThemeSlot> All { get; } =
    [
        new("heading-1", HeadingsGroup, "Level 1 heading text.", LargeOnPage),
        new("heading-2", HeadingsGroup, "Level 2 heading text.", LargeOnPage),
        new("heading-3", HeadingsGroup, "Level 3 heading text.", LargeOnPage),
        new("heading-4", HeadingsGroup, "Level 4 heading text.", OnPage),
        new("heading-5", HeadingsGroup, "Level 5 heading text.", OnPage),
        new("heading-6", HeadingsGroup, "Level 6 heading text, small and uppercase.", OnPage),
        new("heading-rule", HeadingsGroup, "The line under a level 2 heading."),
        new("rule", HeadingsGroup, "A horizontal rule (---)."),

        new("link", LinksGroup, "Link text.", OnPage),
        new("link-underline", LinksGroup, "The quiet line under a link at rest; hover draws it in the link color."),

        new("strong", EmphasisGroup, "Bold text.", OnPage),
        new("emphasis", EmphasisGroup, "Italic text.", OnPage),

        new("list-marker", ListsGroup, "Bullets and the numbers of an ordered list.", OnPage),
        new("task-check", ListsGroup, "A task list checkbox.", new ContrastRule(Page, Contrast.NonText)),
        new("footnote-ref", ListsGroup, "The superscript footnote number in the text.", OnPage),

        new("quote-bar", QuotesGroup, "The bar down the left of a blockquote."),
        new("quote-fill", QuotesGroup, "Behind a blockquote's text; the page color for none."),
        new("quote-text", QuotesGroup, "A blockquote's text.", new ContrastRule("quote-fill", Contrast.Text)),

        .. Callout("note", "A Note callout"),
        .. Callout("tip", "A Tip callout"),
        .. Callout("important", "An Important callout"),
        .. Callout("warning", "A Warning callout"),
        .. Callout("caution", "A Caution callout"),

        new("table-header-fill", TablesGroup, "Behind the header row."),
        new("table-header-text", TablesGroup, "Header row text.", new ContrastRule("table-header-fill", Contrast.Text)),
        new("table-header-rule", TablesGroup, "The heavier line under the header row."),
        new("table-border", TablesGroup, "Cell borders."),
        new("table-stripe", TablesGroup, "Behind every other body row."),

        new("code-inline-text", CodeGroup, "Inline code text.", new ContrastRule("code-inline-fill", Contrast.Text)),
        new("code-inline-fill", CodeGroup, "Behind inline code."),
        new("code-inline-border", CodeGroup, "The outline around inline code."),
        new("code-block-text", CodeGroup, "Code block text that no syntax color claims.", OnCode),
        new("code-block-fill", CodeGroup, "Behind a code block."),
        new("code-block-border", CodeGroup, "The outline around a code block."),

        new("syntax-keyword", SyntaxGroup, "Keywords: if, return, class, and the language's own variables such as this.", OnCode),
        new("syntax-type", SyntaxGroup, "Type names.", OnCode),
        new("syntax-function", SyntaxGroup, "Function, method and class names where they are declared.", OnCode),
        new("syntax-variable", SyntaxGroup, "Variables.", OnCode),
        new("syntax-number", SyntaxGroup, "Numbers and literals, attributes, operators, selectors, and a markdown section.", OnCode),
        new("syntax-string", SyntaxGroup, "Strings and regular expressions.", OnCode),
        new("syntax-builtin", SyntaxGroup, "Built-ins and symbols, and a list bullet inside a markdown block.", OnCode),
        new("syntax-comment", SyntaxGroup, "Comments.", OnCode),
        new("syntax-tag", SyntaxGroup, "Tag and element names, and quotes.", OnCode),
        new("syntax-punctuation", SyntaxGroup, "Punctuation.", OnCode),
        new("syntax-addition", SyntaxGroup, "An added line in a diff.", new ContrastRule("syntax-addition-fill", Contrast.Text)),
        new("syntax-addition-fill", SyntaxGroup, "Behind an added line in a diff."),
        new("syntax-deletion", SyntaxGroup, "A removed line in a diff.", new ContrastRule("syntax-deletion-fill", Contrast.Text)),
        new("syntax-deletion-fill", SyntaxGroup, "Behind a removed line in a diff."),

        new("mark-fill", HighlightGroup, "Behind ==highlighted== text.", new ContrastRule(BodyText, Contrast.Text)),

        new("diagram-primary", DiagramsGroup, "Diagram node fill."),
        new("diagram-primary-border", DiagramsGroup, "Diagram node outline."),
        new("diagram-primary-text", DiagramsGroup, "Text in a diagram node.", new ContrastRule("diagram-primary", Contrast.Text)),
        new("diagram-secondary", DiagramsGroup, "Second node fill, which mermaid uses for alternates and some diagram kinds."),
        new("diagram-tertiary", DiagramsGroup, "Third node fill, used for clusters and backgrounds."),
        new("diagram-line", DiagramsGroup, "Diagram lines and arrows."),
        new("diagram-note", DiagramsGroup, "Diagram note fill."),
        new("diagram-note-text", DiagramsGroup, "Text in a diagram note.", new ContrastRule("diagram-note", Contrast.Text)),
    ];

    private static readonly Dictionary<string, ThemeSlot> ById =
        All.ToDictionary(slot => slot.Id, StringComparer.Ordinal);

    /// <summary>True when the id is a slot.</summary>
    public static bool IsSlot(string id) => ById.ContainsKey(id);

    /// <summary>The slot with this id, or null.</summary>
    public static ThemeSlot? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>The slots a theme must name: all of them, less the diagrams for a stock-diagram theme.</summary>
    public static IEnumerable<ThemeSlot> RequiredFor(bool stockDiagrams) =>
        stockDiagrams ? All.Where(slot => slot.Group != DiagramsGroup) : All;

    private static ThemeSlot[] Callout(string kind, string name)
    {
        string fill = $"callout-{kind}-fill";

        return
        [
            new($"callout-{kind}-bar", CalloutsGroup, $"{name}'s bar down the left edge."),
            new(fill, CalloutsGroup, $"Behind {LowerFirst(name)}'s text."),
            new($"callout-{kind}-title", CalloutsGroup, $"{name}'s title and icon.", new ContrastRule(fill, Contrast.Text)),
        ];
    }

    private static string LowerFirst(string text) =>
        string.Concat(text[..1].ToLowerInvariant(), text[1..]);
}
