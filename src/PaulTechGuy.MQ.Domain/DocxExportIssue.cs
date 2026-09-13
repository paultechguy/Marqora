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
public sealed record DocxExportIssue
{
    /// <summary>
    /// The line in the markdown, counted from one - or zero for something the document has no
    /// single place for, which sorts to the top and shows no line at all.
    /// </summary>
    public int Line { get; init; }

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
