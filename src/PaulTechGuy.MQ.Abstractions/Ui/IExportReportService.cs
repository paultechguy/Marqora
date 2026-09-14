// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// One export's worth of what could not be carried across, and enough about the export to say
/// so properly.
///
/// The document is named by id as well as by name because the report outlives the moment: it
/// is a window rather than a prompt, and by the time a reader picks a row the active tab may
/// be a different document entirely.
/// </summary>
public sealed class ExportIssueReport
{
    /// <summary>
    /// What was exported, for the window's caption and its report: a document's name, or a
    /// Folio's.
    /// </summary>
    public required string DocumentName { get; init; }

    /// <summary>
    /// The text of every document this report describes, by id.
    ///
    /// Held for one purpose: to notice when a document has moved on beneath the report. An edit
    /// allocates a new string, so reference equality is the whole test - and an edit that put
    /// the text back as it was leaves nothing to say. The same test Find All uses.
    ///
    /// A map rather than a single string because a Folio names several documents at once, and
    /// editing one of twelve must not stop the other eleven's rows from working. Each row goes
    /// stale on its own, by the id it carries.
    /// </summary>
    public required IReadOnlyDictionary<Guid, string> ExportedText { get; init; }

    /// <summary>Where the file was written, named in the report so a copy of it is self-contained.</summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// What happened, as the heading: "Folio created", "Word document exported".
    ///
    /// The heading used to open with "Unable to export these items", which is a poor first
    /// sentence for a window that appears only after a perfectly good file has been written -
    /// the reader's first conclusion is that nothing was produced. The outcome leads; what is
    /// missing from it follows underneath.
    /// </summary>
    public required string Outcome { get; init; }

    /// <summary>Things that are genuinely missing or broken.</summary>
    public int FailureCount => Issues.Count(i => !i.IsAdvisory);

    /// <summary>Things worth knowing that are not faults.</summary>
    public int AdvisoryCount => Issues.Count(i => i.IsAdvisory);

    /// <summary>What could not be carried across, in document order.</summary>
    public required IReadOnlyList<ExportIssue> Issues { get; init; }
}

/// <summary>
/// Shows what an export left out.
///
/// A window rather than a prompt, and that is the whole point of it. A list of things to fix
/// is worked through, not read once and dismissed: it stays up beside the editor, each row
/// takes the caret to the line it is about, and the whole report copies to the clipboard as
/// text. A prompt could do none of that, and capped itself at ten items besides.
/// </summary>
public interface IExportReportService
{
    /// <summary>
    /// Puts the report on screen. Returns as soon as it is up - nothing waits on it, because
    /// nothing in Marqora is modal.
    /// </summary>
    /// <param name="goToLine">
    /// Takes the editor to a line in a named document, counted from one. Called when the reader
    /// picks a row, and not called at all once <em>that</em> document has changed underneath the
    /// report: the line numbers describe the documents as they were exported, and a click that
    /// landed on whatever had since moved into that line would be worse than no click at all.
    /// </param>
    void Show(ExportIssueReport report, Action<Guid, int> goToLine);
}
