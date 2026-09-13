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
    /// <summary>The document as it was exported, for the window's caption and its report.</summary>
    public required string DocumentName { get; init; }

    /// <summary>Which open document the line numbers belong to.</summary>
    public required Guid DocumentId { get; init; }

    /// <summary>
    /// The text that was exported.
    ///
    /// Held for one purpose: to notice when the document has moved on beneath the report. An
    /// edit allocates a new string, so reference equality is the whole test - and an edit that
    /// put the text back as it was leaves nothing to say. The same test Find All uses.
    /// </summary>
    public required string ExportedText { get; init; }

    /// <summary>Where the file was written, named in the report so a copy of it is self-contained.</summary>
    public required string OutputPath { get; init; }

    /// <summary>What could not be carried across, in document order.</summary>
    public required IReadOnlyList<DocxExportIssue> Issues { get; init; }
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
    /// Takes the editor to a line, counted from one. Called when the reader picks a row, and
    /// not called at all once the document has changed underneath the report: the line numbers
    /// describe the document as it was exported, and a click that landed on whatever had since
    /// moved into that line would be worse than no click at all.
    /// </param>
    void Show(ExportIssueReport report, Action<int> goToLine);
}
