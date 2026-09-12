// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What is wrong with a link, which decides what can be offered to fix it.
///
/// A kind rather than a message string because the three are repaired differently: an image
/// can be browsed for or pasted over, a link can be pointed at another file, and an anchor can
/// only ever be one of the headings this document already has.
/// </summary>
public enum LinkFindingKind
{
    /// <summary>An image whose file is not there.</summary>
    MissingImage = 0,

    /// <summary>A relative link to a file that is not there.</summary>
    BrokenLink = 1,

    /// <summary>A "#" link naming nothing in this document.</summary>
    DeadAnchor = 2,

    /// <summary>
    /// An image that is there and says nothing about itself.
    ///
    /// The odd one out: nothing is broken, and there is no repair to offer beyond typing. It
    /// travels with the others because what they share is the thing that matters here - a
    /// message drawn in the source pane without a Monaco marker behind it. A marker would bring
    /// the "View Problem" row and the untrue "No quick fixes available" line back for this one
    /// finding, which is exactly the chrome the rest of this type exists to avoid.
    /// </summary>
    MissingAltText = 3,

    /// <summary>
    /// A picture, video or frame whose address is on the web.
    ///
    /// Not a fault, which is what makes it the fourth kind of claim rather than a variation on
    /// the first. The address is valid, the file is there, the author meant every character of
    /// it, and on GitHub it renders. The only thing that is true is that Marqora will not go and
    /// fetch it - so the message says that, and says where it does work, because a mark that
    /// reads as an accusation gets switched off.
    ///
    /// Never reported for a link. A link is a navigation and nothing is fetched until somebody
    /// clicks, which is the line this whole rule is drawn on.
    /// </summary>
    RemoteMedia = 4,

    /// <summary>
    /// A picture whose file is somewhere else on this machine, rather than beside the document.
    ///
    /// Distinct from <see cref="MissingImage"/> because the difference is the whole point: that
    /// one says the file is not there, and for years it said so about files that were sitting
    /// happily on the disk a folder away. Telling somebody to go and find a file that was never
    /// lost is worse than saying nothing.
    ///
    /// It also has the best repair in the app behind it - copying the file in beside the document
    /// and repointing the reference, which needs no network and leaves the document able to
    /// travel.
    /// </summary>
    OutsideFolder = 5,
}

/// <summary>
/// One link that leads nowhere, and where it is.
///
/// Deliberately not a <see cref="Diagnostic"/>, for the same reason <see cref="SpellingIssue"/>
/// is not one. A Diagnostic carries a severity that maps onto Monaco's marker scale, and a
/// marker brings a hover repeating what the squiggle already said, an Alt+F8 peek panel, and a
/// "No quick fixes available" line that is untrue here as well - the fixes are on the
/// right-click menu. These are drawn as decorations instead, so what crosses the bridge is the
/// domain type rather than a marker.
///
/// The suggestions are not carried. Working out what the author meant costs a folder listing or
/// a walk over the headings, and doing it for every finding as the document is checked would be
/// hundreds of calls to fill a menu that is opened once. They are computed when the menu opens,
/// exactly as spelling suggestions are.
///
/// Positions are zero-based, like everything else inside the app.
/// </summary>
public sealed record LinkFinding
{
    public required int Line { get; init; }

    /// <summary>Column the whole reference starts at - the "!" of an image, the "[" of a link.</summary>
    public required int Start { get; init; }

    /// <summary>How many characters the whole reference occupies, which is what gets underlined.</summary>
    public required int Length { get; init; }

    /// <summary>
    /// The target exactly as written, including any query or fragment.
    ///
    /// Carried rather than re-read from the line, because whoever offers suggestions has the
    /// finding and not the text - the same reason <see cref="SpellingIssue.Word"/> is carried.
    /// </summary>
    public required string Url { get; init; }

    public required LinkFindingKind Kind { get; init; }

    /// <summary>
    /// What to say on hover. Built here rather than in the shell so the wording lives beside
    /// the check that decided it, and so a test can assert on it.
    /// </summary>
    public required string Message { get; init; }
}
