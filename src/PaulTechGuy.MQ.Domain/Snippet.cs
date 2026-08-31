// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>Which menu a snippet belongs under.</summary>
public enum SnippetGroup
{
    /// <summary>Markdown constructs: front matter, footnotes, tables and the like.</summary>
    General = 0,

    /// <summary>Mermaid diagrams, which get their own button because the syntax is the
    /// least memorable thing the app renders.</summary>
    Diagram = 1,
}

/// <summary>
/// Something insertable, named for a menu.
///
/// <see cref="Body"/> is plain markdown. Built-in snippets carry theirs directly; a user's
/// comes from a file and is read at the moment it is inserted rather than cached, so
/// editing one in another editor takes effect without restarting anything.
/// </summary>
public sealed record Snippet
{
    public required string Name { get; init; }

    public required SnippetGroup Group { get; init; }

    /// <summary>The text to insert, or null when it still has to be read from <see cref="Path"/>.</summary>
    public string? Body { get; init; }

    /// <summary>Where the file lives, for a user's snippet. Null for a built-in one.</summary>
    public string? Path { get; init; }

    public bool IsBuiltIn => Path is null;
}
