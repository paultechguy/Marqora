// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// One thing an export could not carry across, and where in the document it was.
///
/// A record rather than the sentence it used to be, for one reason: a reader who is told
/// *what* is missing still has to find it. "A 1-pixel transparent PNG (not found)" in a
/// document of two thousand lines is a hunt. Split into a line, a reason and the thing it
/// happened to, the same information can be listed in columns, sorted, copied as text, and
/// clicked to take the editor there.
///
/// <see cref="Line"/> is counted from one, the way an editor counts and the way a person
/// reads. Markdig counts from zero, so the conversion happens once, where the issue is
/// recorded, and nothing downstream has to remember which convention it is holding.
/// </summary>
public sealed record ExportIssue
{
    /// <summary>
    /// The line in the markdown, counted from one - or zero for something the document has no
    /// single place for, which sorts to the top and shows no line at all.
    /// </summary>
    public int Line { get; init; }

    /// <summary>
    /// Which document the line belongs to.
    ///
    /// A Word export has one document and every issue carries the same id. A Folio has as many
    /// as the author ticked, so the id has to travel per issue rather than per report - it is
    /// what a row navigates by, and what decides whether that row has gone stale while its
    /// neighbors are still good.
    /// </summary>
    public Guid DocumentId { get; init; }

    /// <summary>
    /// The document's name, shown on the row only when a report spans more than one. Empty for
    /// a single-document report, where repeating it on every line would say nothing.
    /// </summary>
    public string DocumentName { get; init; } = string.Empty;

    /// <summary>
    /// Worth knowing, but nothing went wrong.
    ///
    /// The distinction is whether anything is <em>broken</em>. A picture that is not on the
    /// machine is a fault; an iframe that stays on the web is not - it could never have been
    /// carried inside a file, it works perfectly in the artifact, and the only reason to
    /// mention it is that the person who opens the file will fetch it themselves.
    ///
    /// Counted apart from the rest, so a heading cannot announce four failures when two of them
    /// are the feature behaving exactly as designed. False by default: a Word export's issues
    /// are all genuine, and so is anything added later that forgets to think about this.
    /// </summary>
    public bool IsAdvisory { get; init; }

    /// <summary>
    /// What went wrong, as a phrase a reader can act on: "Not on this machine", "No Word form
    /// for this element". Sentence case, because it is read as a heading of its own rather
    /// than inside a sentence.
    /// </summary>
    public required string Problem { get; init; }

    /// <summary>
    /// What it happened to: a picture's alt text, the first line of an equation, the name of
    /// a diagram. Never the raw URL of an embedded image - a data URI is thousands of
    /// characters and this is read by a person.
    /// </summary>
    public required string Item { get; init; }

    /// <summary>
    /// The one-line form, for a log or a plain-text copy of the report.
    /// </summary>
    public override string ToString() =>
        Line > 0 ? $"Line {Line}: {Problem} - {Item}" : $"{Problem} - {Item}";
}
