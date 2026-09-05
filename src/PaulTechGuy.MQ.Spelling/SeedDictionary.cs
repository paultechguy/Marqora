// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;

namespace PaulTechGuy.MQ.Spelling;

/// <summary>
/// Words Marqora knows about itself, and the vocabulary of the documents it is for.
///
/// This list is load-bearing rather than a convenience. "Marqora" is a single capitalised word
/// with no interior capital, so <see cref="WordPolicy"/> does not exempt it and no Windows
/// dictionary contains it. It appears 26 times in the project README and 21 times in the welcome
/// document — which is the first thing a new user ever opens. Without this list, spell checking
/// on by default would greet everyone with roughly twenty-seven red underlines, most of them
/// beneath the product's own name.
///
/// It is code rather than data on purpose: these are not the user's words, they are the app's.
/// Nothing here is ever written into the user's dictionary file, so "Restore defaults" and a
/// hand-edited word list both leave it exactly as it is.
///
/// Kept deliberately short. Every entry is a word that will otherwise be flagged and should not
/// be; anything merely uncommon belongs in the user's own list, not here.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Dictionary is the domain word here, matching IUserDictionary and the "
        + "user-facing Add to Dictionary command.")]
public static class SeedDictionary
{
    private static readonly HashSet<string> Words = new(StringComparer.OrdinalIgnoreCase)
    {
        // The product, and what it is built on.
        "Marqora", "Markdig", "Monaco", "Mermaid", "KaTeX", "WinUI", "WebView",
        "Serilog", "NuGet", "Chromium", "Apache",

        // Markdown's own vocabulary.
        "markdown", "frontmatter", "blockquote", "blockquotes", "codeblock", "autolink",
        "autolinks", "permalink", "slug", "slugs", "backtick", "backticks", "fenced",
        "unordered", "renderer", "renderers", "syntaxes",

        // The application's vocabulary, as its own documents use it.
        "autosave", "changelog", "tooltip", "tooltips", "toolbar", "toolbars", "scrollbar",
        "checkbox", "checkboxes", "dropdown", "whitespace", "wordwrap", "cheatsheet",
        "gutter", "minimap", "squiggle", "squiggles", "unindent", "reflow",

        // Words that appear constantly in software prose and are not always in a dictionary.
        "filesystem", "filepath", "hostname", "username", "subfolder", "subfolders",
        "plaintext", "runtime", "workflow", "workflows", "lookup", "lookups", "config",
        "configs", "repo", "repos", "async", "enum", "enums", "boolean", "unpackaged",
    };

    /// <summary>
    /// Whether Marqora ships knowing this word. Case-insensitive, so a word at the start of a
    /// sentence is the same word.
    /// </summary>
    public static bool Contains(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        return Words.Contains(word);
    }

    /// <summary>How many words ship built in. Reported by the preferences page, and by tests.</summary>
    public static int Count => Words.Count;
}
