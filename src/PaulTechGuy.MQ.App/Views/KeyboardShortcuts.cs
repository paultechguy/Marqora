// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.App.Views;

/// <summary>One shortcut, as it is shown and as it is copied.</summary>
internal sealed record Shortcut(string Action, string Keys);

/// <summary>A titled run of shortcuts.</summary>
internal sealed record ShortcutGroup(string Name, IReadOnlyList<Shortcut> Shortcuts);

/// <summary>
/// Every shortcut the app answers to, for Help, Keyboard Shortcuts.
///
/// Written out here rather than gathered from the accelerators at runtime: the real
/// bindings live in three places — the window's accelerators, Monaco's own, and the menus —
/// and several of them are the same command reached by different routes. A list that
/// reflected the wiring would describe the plumbing rather than what a person can press.
///
/// It does have to be kept in step by hand. RegisterAccelerators in MainWindow and
/// HOST_SHORTCUTS in webshell/app.js are the other two halves - the chrome's keyboard and
/// the WebView's; change either and change this.
/// </summary>
internal static class KeyboardShortcuts
{
    public static IReadOnlyList<ShortcutGroup> Groups { get; } =
    [
        new("Menus",
        [
            new("Focus the menu bar", "Alt"),
            new("File menu", "Alt+F"),
            new("Edit menu", "Alt+E"),
            new("Format menu", "Alt+O"),
            new("View menu", "Alt+V"),
            new("Tools menu", "Alt+T"),
            new("Help menu", "Alt+H"),
        ]),

        new("Files",
        [
            new("New tab", "Ctrl+N or Ctrl+T"),
            new("Open...", "Ctrl+O"),
            new("Open folder...", "Ctrl+Shift+O"),
            new("Save", "Ctrl+S"),
            new("Save all", "Ctrl+Shift+S"),
            new("Save as...", "Ctrl+Alt+S"),
            new("Close tab", "Ctrl+W"),
            new("Close all tabs", "Ctrl+Shift+W"),
            new("Print...", "Ctrl+P"),
            new("Preferences...", "Ctrl+,"),
        ]),

        new("Tabs",
        [
            new("Select tab 1 to 8", "Ctrl+1 ... Ctrl+8"),
            new("Select last tab", "Ctrl+9"),
            new("Next tab", "Ctrl+Tab"),
            new("Previous tab", "Ctrl+Shift+Tab"),
        ]),

        new("Clipboard",
        [
            new("Cut", "Ctrl+X"),
            new("Copy", "Ctrl+C"),
            new("Paste", "Ctrl+V"),
            new("Copy as rich text", "Ctrl+Shift+C"),
        ]),

        new("Editing",
        [
            new("Find", "Ctrl+F"),
            new("Find next", "F3"),
            new("Find previous", "Shift+F3"),
            new("Find all", "Ctrl+Shift+F"),
            new("Replace", "Ctrl+H"),
            new("Go to line", "Ctrl+G"),
            new("Undo", "Ctrl+Z"),
            new("Redo", "Ctrl+Y"),
            new("Select all", "Ctrl+A"),
            new("Format document", "Shift+Alt+F"),
            new("Correct the misspelling at the cursor", "Ctrl+."),
        ]),

        new("Formatting",
        [
            new("Bold", "Ctrl+B"),
            new("Italic", "Ctrl+I"),
            new("Strikethrough", "Ctrl+Shift+X"),
            new("Inline code", "Ctrl+`"),
            new("Link", "Ctrl+K"),
            new("Code block", "Ctrl+Shift+K"),
            new("Blockquote", "Ctrl+Shift+."),
            new("Bullet list", "Ctrl+Shift+8"),
            new("Numbered list", "Ctrl+Shift+7"),
            new("Increase heading level", "Ctrl+Shift+]"),
            new("Decrease heading level", "Ctrl+Shift+["),
        ]),

        new("View",
        [
            new("Source only", "Alt+1"),
            new("Split", "Alt+2"),
            new("Preview only", "Alt+3"),

            new("Show or hide the outline", "Alt+4"),
            new("Go to the outline, and back", "Alt+Shift+4"),
            new("Leave the outline", "Escape"),

            new("Spell check", "F7"),

            new("Word wrap", "Alt+Z"),
            new("Zoom the active pane", "Ctrl+= / Ctrl+- / Ctrl+0"),
            new("Zoom both panes", "Ctrl+Shift+= / Ctrl+Shift+- / Ctrl+Shift+0"),
        ]),

        new("Help",
        [
            new("Markdown cheatsheet", "Ctrl+F1"),
        ]),
    ];
}
