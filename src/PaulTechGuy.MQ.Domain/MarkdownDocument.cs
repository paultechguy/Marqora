// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// One open document, which is to say one tab.
///
/// <see cref="Text"/> is the in-memory buffer and <see cref="SavedText"/> is what is on disk,
/// so dirty state is a comparison rather than a mutable flag that can drift out of sync.
///
/// <see cref="Path"/> is null for a document that has never been saved. Those live only in
/// memory until the first save gives them a location.
/// </summary>
public sealed record MarkdownDocument
{
    /// <summary>Identity that survives a rename, so tabs and editor models stay paired.</summary>
    public required Guid Id { get; init; }

    /// <summary>Absolute path on disk, or null for a document that has never been saved.</summary>
    public string? Path { get; init; }

    /// <summary>Name shown for an unsaved document, such as "Untitled 2".</summary>
    public string UntitledName { get; init; } = "Untitled";

    public required string Text { get; init; }

    public required string SavedText { get; init; }

    public required DateTimeOffset LoadedUtc { get; init; }

    /// <summary>
    /// How this document stands in relation to the file behind it. Set by the workspace as it
    /// hears from the file watcher, and read by the tab strip and the change banner.
    /// </summary>
    public ExternalState External { get; init; } = ExternalState.InSync;

    /// <summary>
    /// The file as it was last seen on disk, or null for a document with no file yet. Lets a
    /// watcher event that reports nothing new be discarded before anyone is asked about it.
    /// </summary>
    public FileStamp? Stamp { get; init; }

    /// <summary>
    /// When the workspace last took new content from disk without asking, or null if it never
    /// has.
    ///
    /// Only the automatic reload sets this. A reload the user asked for is not news to them,
    /// and neither is a save, which is why <see cref="AsSaved"/> clears it: once this text has
    /// been written back, "something arrived here that you may not have read" has stopped
    /// being true.
    /// </summary>
    public DateTimeOffset? AutoReloadedUtc { get; init; }

    public bool IsUntitled => Path is null;

    /// <summary>Tab label: the file name, or the placeholder name when never saved.</summary>
    public string DisplayName => Path is null ? UntitledName : System.IO.Path.GetFileName(Path);

    /// <summary>Full path for tooltips, or the placeholder name when there is no file yet.</summary>
    public string DisplayPath => Path ?? UntitledName;

    /// <summary>
    /// Whether there is anything here that disk does not have.
    ///
    /// A missing file counts, and that single clause is the whole of the deleted-file
    /// behaviour: the tab's dot appears, the close prompt offers to save, <c>CanSave</c> turns
    /// on and the buffer is written back on the next Ctrl+S. Faking it by writing a sentinel
    /// into <see cref="SavedText"/> would do the same on the surface and quietly break reload
    /// and the close prompt, which is exactly what a comparison rather than a mutable flag
    /// exists to prevent.
    /// </summary>
    public bool IsDirty => External == ExternalState.Missing
        || !string.Equals(Text, SavedText, StringComparison.Ordinal);

    /// <summary>Whether an external change is waiting for the user to say what to do about it.</summary>
    public bool HasExternalChange => External != ExternalState.InSync;

    public MarkdownDocument WithText(string text) => this with { Text = text };

    /// <summary>
    /// Marks the buffer as written. Clears any external state with it: whatever the file did,
    /// this document has just decided what it holds.
    /// </summary>
    public MarkdownDocument AsSaved(FileStamp? stamp = null) => this with
    {
        SavedText = Text,
        External = ExternalState.InSync,
        Stamp = stamp ?? Stamp,
        AutoReloadedUtc = null,
    };

    /// <summary>A new, empty document that exists only in memory.</summary>
    public static MarkdownDocument CreateUntitled(string untitledName) => new()
    {
        Id = Guid.NewGuid(),
        Path = null,
        UntitledName = untitledName,
        Text = string.Empty,
        SavedText = string.Empty,
        LoadedUtc = DateTimeOffset.UtcNow,
    };
}
