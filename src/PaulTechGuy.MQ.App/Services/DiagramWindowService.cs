// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Views;
using Windows.Graphics;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Owns the diagram pop-out windows and the rule about what a second double-click does.
/// </summary>
public sealed class DiagramWindowService(
    WindowContext window,
    IWebAssetProvider assets,
    IThemeService theme,
    ILoggerFactory loggerFactory,
    ILogger<DiagramWindowService> logger) : IDiagramWindowService
{
    /// <summary>Big enough for most diagrams without covering the editor entirely.</summary>
    private const int DefaultWidth = 900;
    private const int DefaultHeight = 700;

    /// <summary>Matches the window's own minimum, so a small main window cannot undercut it.</summary>
    private const int MinimumExtent = 320;

    /// <summary>
    /// Successive windows are offset so the second does not land exactly on the first. The
    /// step wraps after a handful, which is enough to keep a small pile distinguishable
    /// without marching off the screen.
    /// </summary>
    private const int CascadeStep = 32;
    private const int CascadeWrap = 8;

    private readonly Dictionary<Guid, DiagramWindow> _windows = [];

    /// <summary>
    /// Diagrams whose window is still being built, so a second double-click on the same one
    /// does not open a duplicate during the second a WebView takes to come up.
    /// </summary>
    private readonly HashSet<(Guid Document, string Hash)> _opening = [];

    private int _cascade;

    public int OpenCount => _windows.Count;

    public IReadOnlyCollection<DiagramWatch> Watched =>
        [.. _windows.Values.Select(open => new DiagramWatch(open.Id, open.DocumentId, open.Hash))];

    public event EventHandler<int>? OpenCountChanged;

    public event EventHandler? WatchedChanged;

    public async Task ShowAsync(
        Guid documentId,
        int index,
        string hash,
        string svg,
        string documentName,
        string documentPath)
    {
        // Matched on the definition the window is currently following, which the preview
        // keeps up to date as the diagram is edited. Matching on the definition it was first
        // opened with would miss, and open a second window onto the same diagram.
        if (Existing(documentId, hash) is { } already)
        {
            logger.LogDebug("Raising the window already following diagram {Id}.", already.Id);
            already.Update(hash, index, svg);
            already.Raise();
            return;
        }

        if (!_opening.Add((documentId, hash)))
        {
            logger.LogDebug("Ignoring a second request for that diagram; its window is opening.");
            return;
        }

        DiagramWindow? opened = null;

        try
        {
            opened = new DiagramWindow(
                assets,
                theme,
                Guid.NewGuid(),
                documentId,
                hash,
                Title(index, documentName),
                documentName,
                documentPath,
                svg,
                loggerFactory.CreateLogger<DiagramWindow>());

            opened.Dismissed += OnDismissed;

            _windows[opened.Id] = opened;

            await opened.InitializeAsync(Placement()).ConfigureAwait(true);

            opened.Activate();

            logger.LogInformation("Opened a window for diagram {Id}. {Count} now open.", opened.Id, _windows.Count);
            Changed();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A diagram window could not be opened.");

            if (opened is not null)
            {
                opened.Dismissed -= OnDismissed;
                _windows.Remove(opened.Id);
            }
        }
        finally
        {
            _opening.Remove((documentId, hash));
        }
    }

    /// <summary>
    /// Reports only arrive for diagrams being watched, but a window can close between the
    /// preview deciding to send one and it arriving here, so an unknown id is dropped.
    /// </summary>
    public void Update(Guid diagramId, string hash, int index, string svg)
    {
        if (_windows.TryGetValue(diagramId, out DiagramWindow? open))
        {
            // The definition it follows has moved on; the watch list has to follow, or the
            // preview would resume tracking from a definition that no longer exists.
            bool wasFollowing = string.Equals(open.Hash, hash, StringComparison.Ordinal);

            open.Update(hash, index, svg);

            if (!wasFollowing)
            {
                WatchedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void MarkRemoved(Guid diagramId)
    {
        if (_windows.TryGetValue(diagramId, out DiagramWindow? open))
        {
            logger.LogDebug("Diagram {Id} is no longer in its document.", diagramId);
            open.MarkRemoved();
        }
    }

    public void MarkInvalid(Guid diagramId, string message)
    {
        if (_windows.TryGetValue(diagramId, out DiagramWindow? open))
        {
            open.MarkInvalid(message);
        }
    }

    public void CloseAll()
    {
        if (_windows.Count == 0)
        {
            return;
        }

        logger.LogInformation("Closing {Count} diagram window(s).", _windows.Count);

        // Copied first: closing raises Dismissed, which mutates the dictionary being walked.
        foreach (DiagramWindow open in _windows.Values.ToArray())
        {
            open.Dismissed -= OnDismissed;

            try
            {
                open.Shutdown();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "A diagram window did not close cleanly.");
            }
        }

        _windows.Clear();
        _cascade = 0;

        Changed();
    }

    public void Shutdown() => CloseAll();

    private DiagramWindow? Existing(Guid documentId, string hash) =>
        _windows.Values.FirstOrDefault(open =>
            open.DocumentId == documentId && string.Equals(open.Hash, hash, StringComparison.Ordinal));

    private void OnDismissed(object? sender, EventArgs e)
    {
        if (sender is not DiagramWindow closed)
        {
            return;
        }

        closed.Dismissed -= OnDismissed;

        if (_windows.Remove(closed.Id))
        {
            logger.LogDebug("A diagram window closed. {Count} still open.", _windows.Count);
            Changed();
        }
    }

    private void Changed()
    {
        OpenCountChanged?.Invoke(this, _windows.Count);
        WatchedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Names the window after the document it came from, in the same shape as the main
    /// window's own title, and numbers the diagram by where it sits in that document.
    ///
    /// Both halves earn their place once a window can outlive its source: the file name is
    /// the only thing left saying what was closed or edited, and the number distinguishes
    /// two windows onto the same file. The number follows the diagram as the document is
    /// edited rather than being frozen at whatever it was when the window opened.
    /// </summary>
    private static string Title(int index, string documentName) =>
        string.IsNullOrWhiteSpace(documentName)
            ? $"Diagram {index + 1}"
            : $"{documentName} - Diagram {index + 1}";

    /// <summary>
    /// Where a new window goes: offset from the main window, stepped so a run of them
    /// cascades instead of landing in one stack.
    /// </summary>
    private RectInt32 Placement()
    {
        int offset = CascadeStep * (_cascade % CascadeWrap);
        _cascade++;

        if (window.Window?.AppWindow is not { } main)
        {
            return new RectInt32(120 + offset, 120 + offset, DefaultWidth, DefaultHeight);
        }

        return new RectInt32(
            main.Position.X + CascadeStep + offset,
            main.Position.Y + CascadeStep + offset,
            Math.Min(DefaultWidth, Math.Max(MinimumExtent, main.Size.Width - CascadeStep)),
            Math.Min(DefaultHeight, Math.Max(MinimumExtent, main.Size.Height - CascadeStep)));
    }
}
