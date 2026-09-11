// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What a launch with no file on the command line opens.
///
/// <see cref="RestoreSession"/> is member zero because it is what the app has always done,
/// and a settings file written before this preference existed must keep doing it.
/// </summary>
public enum StartupBehavior
{
    /// <summary>Reopen the documents that were on screen when the app last closed.</summary>
    RestoreSession = 0,

    /// <summary>Start on a single empty, untitled tab.</summary>
    EmptyTab = 1,

    /// <summary>Open the welcome document.</summary>
    WelcomeDocument = 2,
}

/// <summary>
/// When a modified document is written back without being asked.
///
/// <see cref="Off"/> is member zero: autosave is the kind of thing that must be opted into,
/// never inherited by surprise from an upgrade.
/// </summary>
public enum AutoSaveMode
{
    /// <summary>Never. Documents are saved only when asked.</summary>
    Off = 0,

    /// <summary>Save when the window loses focus, and when a tab is switched away from.</summary>
    OnFocusLoss = 1,

    /// <summary>Save once typing has paused for the configured number of seconds.</summary>
    AfterDelay = 2,
}

/// <summary>
/// Whether headings are numbered, and which level starts the count.
///
/// One preference for both places a heading is shown: the preview and the outline panel.
/// The panel follows this rather than carrying a switch of its own, so that a section named
/// "2.3" in one is named "2.3" in the other.
///
/// The numbers are added to the rendered copy, never to the document: the markdown source is
/// not rewritten, so nothing here can damage a file. See <see cref="HeadingNumbers"/> for the
/// rule, and HeadingNumberPass for where it is written in.
///
/// The level named is the one that becomes "1", "2", "3"; every level below it becomes a
/// further component, so <see cref="FromHeading2"/> gives 1, 1.1, 1.1.1 starting at h2 and
/// leaves the document's title unnumbered.
///
/// A heading above the chosen level is not numbered, but it does begin a new section: every
/// level beneath it starts again from one. So with <see cref="FromHeading2"/>, each "#"
/// chapter restarts its own "##" numbering rather than the count running on through the
/// whole document.
///
/// The member values are the heading levels themselves, which <see cref="HeadingNumbers"/>
/// relies on: the cast from this enum to the level the count starts at is the mapping rather
/// than a coincidence.
/// </summary>
public enum HeadingNumbering
{
    Off = 0,
    FromHeading1 = 1,
    FromHeading2 = 2,
    FromHeading3 = 3,
}

/// <summary>
/// Where a pasted image is written, relative to the document it is pasted into.
///
/// <see cref="DocumentAssets"/> is member zero and the default. A folder per document makes
/// "which images belong to this one" answerable from the path alone, which is what a Save As
/// that has to bring them along, and any later report of images nothing references, both need.
///
/// The other two exist because a repository usually has a convention already. Pasting into a
/// README that has sat beside a docs/images folder for three years should not have to create
/// README.assets next to it.
/// </summary>
public enum ImageFolderMode
{
    /// <summary>A folder per document: "guide.md" gets "guide.assets".</summary>
    DocumentAssets = 0,

    /// <summary>One "images" folder beside the document, shared by every document in it.</summary>
    SharedImages = 1,

    /// <summary>Directly beside the document, with no folder at all.</summary>
    BesideDocument = 2,
}
