// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// The snippets that ship with the app.
///
/// A table in code rather than files on disk: nothing to deploy, nothing for
/// build/Get-WebAssets.ps1 to fetch, and no way for a half-installed folder to make a
/// feature disappear. The user's own snippets come from a folder instead, and one of
/// theirs with the same name quietly wins.
///
/// <c>$0</c> marks where the caret lands. Everything else is plain markdown, because these
/// are also the worked examples for what a user's own snippet file may contain.
/// </summary>
internal static class BuiltInSnippets
{
    public static IReadOnlyList<Snippet> All { get; } =
    [
        // General, in name order, so this list reads the way the menu does. Nothing here is
        // reached for often enough to earn a place at the top, and a user hunting one has
        // only its name to go on. The Insert menu sorts anyway - that is what slots the code
        // block and the table in among these - but a reader of this file should not have to
        // know that to predict what the menu shows.
        General("Abbreviation", "The $0 specification.\n\n*[$0]: Spelled out here"),
        General("Collapsible Section", "<details>\n<summary>$0</summary>\n\nHidden until opened.\n\n</details>"),
        General("Definition List", "$0\n:   The definition."),
        General("Footnote", "Here is a claim.[^1]\n\n[^1]: $0"),
        General("Front Matter", "---\ntitle: $0\ndate:\ntags: []\n---"),
        General("Image with Caption", "<figure>\n  <img src=\"$0\" alt=\"\">\n  <figcaption>Caption</figcaption>\n</figure>"),
        // A worked chord rather than a stub: an empty second <kbd> holds no text, so line
        // layout treats the space in front of it as trailing whitespace and drops it, leaving
        // the plus sign with a gap on its left and none on its right. Naming a key keeps the
        // rendering honest from the moment it is inserted. The non-breaking spaces survive
        // that same trimming whatever the keys are edited to, and hold the chord on one line.
        General("Keyboard Keys", "<kbd>Ctrl</kbd>&nbsp;+&nbsp;<kbd>S</kbd>"),
        General("Link Reference", "See [the docs][docs].\n\n[docs]: $0 \"Title\""),
        General("Maths Block", "$$\n$0\n$$"),
        General("Note", "> [!NOTE]\n> $0"),
        General("Warning", "> [!WARNING]\n> $0"),

        // Mermaid. Every one of these is a working diagram rather than a stub, because the
        // point of the menu is to hand over syntax nobody remembers, and a skeleton with
        // the shape left out remembers none of it for you.
        //
        // Curated rather than alphabetical, and left that way on purpose: flowchart and
        // sequence are what people actually reach for, and name order would drop them to
        // third and eighth. This order carries a judgement the alphabet does not.
        Diagram("Flowchart", "```mermaid\nflowchart LR\n    A[Start] --> B{Choice}\n    B -->|yes| C[Done]\n    B -->|no| A\n$0```"),
        Diagram("Sequence", "```mermaid\nsequenceDiagram\n    Alice->>Bob: Question\n    Bob-->>Alice: Answer\n$0```"),
        Diagram("Class", "```mermaid\nclassDiagram\n    class Document {\n        +string Title\n        +Save()\n    }\n    Document <|-- Markdown\n$0```"),
        Diagram("State", "```mermaid\nstateDiagram-v2\n    [*] --> Draft\n    Draft --> Review\n    Review --> Published\n    Published --> [*]\n$0```"),
        Diagram("Entity Relationship", "```mermaid\nerDiagram\n    AUTHOR ||--o{ DOCUMENT : writes\n    DOCUMENT ||--|{ SECTION : contains\n$0```"),
        Diagram("Gantt", "```mermaid\ngantt\n    title Schedule\n    dateFormat YYYY-MM-DD\n    section Work\n    Draft :a1, 2026-01-01, 7d\n    Review :after a1, 3d\n$0```"),
        Diagram("Pie", "```mermaid\npie title Where the time goes\n    \"Writing\" : 45\n    \"Editing\" : 30\n    \"Everything else\" : 25\n$0```"),
        Diagram("Mindmap", "```mermaid\nmindmap\n  root((Document))\n    Structure\n      Headings\n      Lists\n    Content\n      Prose\n      Diagrams\n$0```"),
        Diagram("Timeline", "```mermaid\ntimeline\n    title Project\n    2026-01 : Started\n    2026-02 : First draft\n    2026-03 : Published\n$0```"),
        Diagram("Git Graph", "```mermaid\ngitGraph\n    commit\n    branch feature\n    commit\n    checkout main\n    merge feature\n$0```"),
    ];

    private static Snippet General(string name, string body) =>
        new() { Name = name, Group = SnippetGroup.General, Body = body };

    private static Snippet Diagram(string name, string body) =>
        new() { Name = name, Group = SnippetGroup.Diagram, Body = body };
}
