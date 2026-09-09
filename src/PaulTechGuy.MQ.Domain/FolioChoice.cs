// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>Which shape a Folio takes on disk.</summary>
public enum FolioForm
{
    /// <summary>
    /// A folder of files. The form for somewhere that is going to be worked in - another
    /// machine, a backup, a repository.
    /// </summary>
    Folder = 0,

    /// <summary>One zip. The form for sending sources.</summary>
    Zip = 1,

    /// <summary>
    /// One HTML file: every document rendered into a single page that opens in any browser,
    /// offline, with nothing installed.
    ///
    /// The form for a reader who may not have Marqora, which is most readers - so it is what
    /// the dialog opens on.
    /// </summary>
    SingleFile = 2,
}

/// <summary>
/// What the author settled on in the preflight.
///
/// Only the documents they left ticked, and which shape they asked for. The plan is made again
/// from this, rather than the dialog handing back the one it was showing: unticking a document
/// changes which images travel and which links now point outside, so a plan built from a
/// different selection would describe a Folio nobody asked for.
/// </summary>
public sealed record FolioChoice
{
    /// <summary>The documents to include, by absolute path.</summary>
    public required IReadOnlyList<string> DocumentPaths { get; init; }

    public required FolioForm Form { get; init; }

    /// <summary>
    /// The width past which images are reduced on the way out, or zero to send them as they are.
    ///
    /// Zero is the default and the conservative answer: it is the only setting in this dialog
    /// that loses something, and what a Folio hands back when it is unpacked is whatever went
    /// into it - so a reduced picture is reduced for good.
    /// </summary>
    public int MaxImageWidth { get; init; }
}
