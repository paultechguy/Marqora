// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Views;
using Windows.Graphics;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Puts an export's warnings on screen.
///
/// A new window each time, like the Folio preflight and unlike Find All: a report describes one
/// particular export of one particular document, and there is nothing in it worth carrying into
/// the next one. Stale advice about a file that has since been exported again is worse than no
/// window at all.
///
/// Nothing waits on it. The export has already finished and the file is already written by the
/// time this is called - the window is there to be worked through afterwards, at whatever pace
/// the reader chooses, while they edit the document behind it.
/// </summary>
public sealed class ExportReportService(
    WindowContext window,
    IWorkspaceService workspace,
    ISettingsService settings,
    IThemeService theme,
    IUiDispatcher ui,
    ILoggerFactory loggerFactory,
    ILogger<ExportReportService> logger) : IExportReportService
{
    public void Show(ExportIssueReport report, Action<int> goToLine)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(goToLine);

        if (report.Issues.Count == 0)
        {
            return;
        }

        if (window.Window is null)
        {
            logger.LogWarning("Cannot show the export report: no window is available yet.");
            return;
        }

        try
        {
            var reportWindow = new ExportReportWindow(
                report,
                goToLine,
                workspace,
                settings,
                theme,
                ui,
                window.WindowHandle,
                loggerFactory.CreateLogger<ExportReportWindow>());

            reportWindow.Present(MainWindowBounds());

            logger.LogInformation(
                "Reported {Count} things the export of {Document} could not carry across.",
                report.Issues.Count,
                report.DocumentName);
        }
        catch (Exception ex)
        {
            // The document is already written, so a window that will not open costs the reader
            // the list rather than the export. Said in the log and not in a second dialog.
            logger.LogError(ex, "The export report window could not be opened.");
        }
    }

    /// <summary>Where the editor is, so a first-time report opens over it rather than adrift.</summary>
    private RectInt32 MainWindowBounds() =>
        window.Window?.AppWindow is { } main
            ? new RectInt32(main.Position.X, main.Position.Y, main.Size.Width, main.Size.Height)
            : new RectInt32(120, 120, 1_400, 900);
}
