// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Holds every open document, tracks which is active, and performs all file I/O.
///
/// Each entry is an immutable record, so a change replaces the entry in the list rather than
/// mutating it. Each document on disk gets its own watcher; untitled documents get none,
/// because there is nothing to watch.
/// </summary>
public sealed class DocumentWorkspace : IWorkspaceService, IDisposable
{
    /// <summary>
    /// Above this size a watcher event is judged on the file's stamp alone.
    ///
    /// The content comparison is what stops a touch or an identical rewrite from asking the
    /// user anything, and for markdown it costs a read of a few kilobytes. A file this large
    /// is not one anybody is editing here, and reading it on every watcher event would tie up
    /// the watcher thread for no benefit.
    /// </summary>
    private const long MaxCompareBytes = 4L * 1024 * 1024;

    private readonly IFileWatcherFactory _watcherFactory;
    private readonly ISettingsService _settings;
    private readonly ILogger<DocumentWorkspace> _logger;

    private readonly List<MarkdownDocument> _documents = [];
    private readonly Dictionary<Guid, IFileWatcher> _watchers = [];

    /// <summary>
    /// Per-document encoding, preserved from the file that was read so a document written
    /// without a byte-order mark is not silently given one on save.
    /// </summary>
    private readonly Dictionary<Guid, Encoding> _encodings = [];

    /// <summary>Set while this service is itself writing, so a watcher ignores its own save.</summary>
    private readonly Dictionary<Guid, DateTimeOffset> _suppressWatchUntil = [];

    private Guid _activeId;
    private int _untitledCounter;

    public DocumentWorkspace(
        IFileWatcherFactory watcherFactory,
        ISettingsService settings,
        ILogger<DocumentWorkspace> logger)
    {
        _watcherFactory = watcherFactory;
        _settings = settings;
        _logger = logger;
    }

    public IReadOnlyList<MarkdownDocument> Documents => _documents;

    public MarkdownDocument? Active => Find(_activeId);

    public bool HasDocuments => _documents.Count > 0;

    public event EventHandler<WorkspaceChangedEventArgs>? Changed;

    public MarkdownDocument? Find(Guid id) => _documents.FirstOrDefault(d => d.Id == id);

    // ------------------------------------------------------------------ opening

    public async Task<MarkdownDocument> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);

        // Opening a file that is already open should surface it, not duplicate it.
        MarkdownDocument? existing = _documents.FirstOrDefault(
            d => d.Path is not null && string.Equals(d.Path, fullPath, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            Activate(existing.Id);
            return existing;
        }

        (string text, Encoding encoding, FileStamp? stamp) = await Task
            .Run(() => ReadAllText(fullPath), cancellationToken).ConfigureAwait(false);

        var document = new MarkdownDocument
        {
            Id = Guid.NewGuid(),
            Path = fullPath,
            Text = text,
            SavedText = text,
            LoadedUtc = DateTimeOffset.UtcNow,
            Stamp = stamp,
        };

        _documents.Add(document);
        _encodings[document.Id] = encoding;
        StartWatching(document);

        _logger.LogInformation("Opened {Path} ({Length} characters).", fullPath, text.Length);
        Raise(WorkspaceChange.Opened, document);

        Activate(document.Id);
        return document;
    }

    public MarkdownDocument CreateUntitled(string? text = null)
    {
        MarkdownDocument document = MarkdownDocument.CreateUntitled($"Untitled {++_untitledCounter}");

        // Set before the document is announced, so the tab is opened holding this text. It
        // counts as unsaved from the outset, which is right: there is nothing on disk yet.
        if (!string.IsNullOrEmpty(text))
        {
            document = document.WithText(text);
        }

        _documents.Add(document);

        _logger.LogInformation("Created {Name}.", document.UntitledName);
        Raise(WorkspaceChange.Opened, document);

        Activate(document.Id);
        return document;
    }

    public async Task RestoreAsync(
        IReadOnlyList<string> paths,
        int activeIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var restored = new List<MarkdownDocument>();

        foreach (string path in paths)
        {
            if (!File.Exists(path))
            {
                _logger.LogInformation("Skipping {Path} from the previous session: it no longer exists.", path);
                continue;
            }

            try
            {
                restored.Add(await OpenAsync(path, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not restore {Path} from the previous session.", path);
            }
        }

        if (restored.Count == 0)
        {
            return;
        }

        int index = Math.Clamp(activeIndex, 0, restored.Count - 1);
        Activate(restored[index].Id);

        _logger.LogInformation("Restored {Count} documents from the previous session.", restored.Count);
    }

    // ---------------------------------------------------------------- activation

    public void Activate(Guid id)
    {
        if (_activeId == id || Find(id) is not { } document)
        {
            return;
        }

        _activeId = id;
        Raise(WorkspaceChange.Activated, document);
    }

    public void Move(Guid id, int newIndex)
    {
        int current = _documents.FindIndex(d => d.Id == id);

        if (current < 0 || newIndex < 0 || newIndex >= _documents.Count || current == newIndex)
        {
            return;
        }

        MarkdownDocument document = _documents[current];
        _documents.RemoveAt(current);
        _documents.Insert(newIndex, document);

        Raise(WorkspaceChange.Reordered, document);
    }

    // ------------------------------------------------------------------ editing

    public void ApplyEdit(Guid id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int index = _documents.FindIndex(d => d.Id == id);

        if (index < 0 || string.Equals(_documents[index].Text, text, StringComparison.Ordinal))
        {
            return;
        }

        _documents[index] = _documents[index].WithText(text);
        Raise(WorkspaceChange.Edited, _documents[index]);
    }

    // ------------------------------------------------------------------- saving

    public async Task SaveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        int index = _documents.FindIndex(d => d.Id == id);

        if (index < 0 || _documents[index].Path is not { } path)
        {
            // Never saved: the caller has to choose a location first.
            return;
        }

        ExternalState before = _documents[index].External;

        FileStamp? stamp = await WriteAsync(id, path, _documents[index].Text, cancellationToken)
            .ConfigureAwait(false);

        // AsSaved clears any external state with the write. That is what turns Ctrl+S on a
        // document whose file was deleted back into an ordinary saved document.
        _documents[index] = _documents[index].AsSaved(stamp);

        // A watch that was abandoned when the folder went away has to be set up again; the
        // file the save just created is a different file as far as the watcher is concerned.
        if (before == ExternalState.Missing)
        {
            StartWatching(_documents[index]);
        }

        _logger.LogInformation("Saved {Path}.", path);
        Raise(WorkspaceChange.Saved, _documents[index]);
    }

    public async Task SaveAsAsync(Guid id, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int index = _documents.FindIndex(d => d.Id == id);

        if (index < 0)
        {
            return;
        }

        string fullPath = Path.GetFullPath(path);

        FileStamp? stamp = await WriteAsync(id, fullPath, _documents[index].Text, cancellationToken)
            .ConfigureAwait(false);

        _documents[index] = _documents[index] with
        {
            Path = fullPath,
            SavedText = _documents[index].Text,
            External = ExternalState.InSync,
            Stamp = stamp,
            AutoReloadedUtc = null,
        };

        // The document may have had no path at all, so start watching now.
        StartWatching(_documents[index]);

        _logger.LogInformation("Saved a copy as {Path}.", fullPath);
        Raise(WorkspaceChange.Saved, _documents[index]);
    }

    public Task ReloadAsync(Guid id, CancellationToken cancellationToken = default) =>
        ReloadCoreAsync(id, automatic: false, cancellationToken);

    /// <summary>
    /// Takes what is on disk, either because the user asked or because the watcher found a
    /// change this document had no reason to refuse.
    ///
    /// <paramref name="automatic"/> is the whole difference between the two, and it is here for
    /// one reason: a reload nobody asked for is the only kind worth mentioning afterwards. It
    /// is recorded on the document as <see cref="MarkdownDocument.AutoReloadedUtc"/> rather
    /// than announced from here, because the workspace has no business knowing how - or
    /// whether - the UI chooses to say so.
    ///
    /// A reload the user asked for clears that stamp rather than leaving it alone. They have
    /// just watched this document be replaced by their own hand; telling them about it later
    /// would be reporting their own action back to them.
    /// </summary>
    private async Task ReloadCoreAsync(Guid id, bool automatic, CancellationToken cancellationToken)
    {
        int index = _documents.FindIndex(d => d.Id == id);

        if (index < 0 || _documents[index].Path is not { } path)
        {
            return;
        }

        (string text, Encoding encoding, FileStamp? stamp) = await Task
            .Run(() => ReadAllText(path), cancellationToken).ConfigureAwait(false);

        _encodings[id] = encoding;

        _documents[index] = _documents[index] with
        {
            Text = text,
            SavedText = text,
            LoadedUtc = DateTimeOffset.UtcNow,
            External = ExternalState.InSync,
            Stamp = stamp,
            AutoReloadedUtc = automatic ? DateTimeOffset.UtcNow : null,
        };

        _logger.LogInformation("Reloaded {Path} from disk.", path);
        Raise(WorkspaceChange.ReloadedFromDisk, _documents[index]);
    }

    /// <summary>
    /// "Keep Mine": the user has looked at a pending change and decided the buffer wins.
    ///
    /// Only clears the marker. The text is not touched, and the document stays dirty against
    /// what is on disk, so the next external write asks again - which is right, because it
    /// will be a different change from the one just dismissed.
    ///
    /// A missing file is left alone. Its marker is not a question.
    /// </summary>
    public void ResolveExternalChange(Guid id)
    {
        int index = _documents.FindIndex(d => d.Id == id);

        if (index < 0 || _documents[index].External != ExternalState.Changed)
        {
            return;
        }

        _documents[index] = _documents[index] with { External = ExternalState.InSync };

        _logger.LogInformation("Kept the buffer for {Path} over the version on disk.", _documents[index].DisplayPath);
        Raise(WorkspaceChange.ExternalStateChanged, _documents[index]);
    }

    // ------------------------------------------------------------------ closing

    public void Close(Guid id)
    {
        int index = _documents.FindIndex(d => d.Id == id);

        if (index < 0)
        {
            return;
        }

        MarkdownDocument document = _documents[index];

        StopWatching(id);
        _encodings.Remove(id);
        _suppressWatchUntil.Remove(id);
        _documents.RemoveAt(index);

        _logger.LogInformation("Closed {Name}.", document.DisplayName);
        Changed?.Invoke(this, new WorkspaceChangedEventArgs(WorkspaceChange.Closed, null, id));

        if (_activeId != id)
        {
            return;
        }

        _activeId = Guid.Empty;

        // Activate the neighbour that took its place, or the new last document.
        if (_documents.Count > 0)
        {
            Activate(_documents[Math.Min(index, _documents.Count - 1)].Id);
        }
    }

    // ------------------------------------------------------------------- file io

    /// <summary>
    /// Reads the file while allowing other processes to keep writing, so a document being
    /// generated by another tool can still be opened. Returns the detected encoding so a
    /// later save can reproduce it, and the stamp the file carried while it was open.
    ///
    /// The stamp is taken from the open handle rather than looked up afterwards, so it
    /// describes the bytes that were actually read even if the file is rewritten a moment
    /// later.
    /// </summary>
    private static (string Text, Encoding Encoding, FileStamp? Stamp) ReadAllText(string path)
    {
        using FileStream stream = File.Open(
            path,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
                Options = FileOptions.SequentialScan,
            });

        var stamp = new FileStamp(File.GetLastWriteTimeUtc(path), stream.Length);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string text = reader.ReadToEnd();

        return (text, reader.CurrentEncoding, stamp);
    }

    /// <summary>The file as it stands right now, or null when it is not there.</summary>
    private static FileStamp? StampOf(string path)
    {
        var info = new FileInfo(path);

        return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : null;
    }

    private async Task<FileStamp?> WriteAsync(Guid id, string path, string text, CancellationToken cancellationToken)
    {
        // The watcher would otherwise report this write as an external change. The window is
        // a first line of defence only - a slow share can outrun it, which is what the stamp
        // recorded below is for.
        _suppressWatchUntil[id] = DateTimeOffset.UtcNow.AddSeconds(2);

        Encoding encoding = _encodings.TryGetValue(id, out Encoding? known)
            ? known
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        _encodings[id] = encoding;

        await File.WriteAllTextAsync(path, text, encoding, cancellationToken).ConfigureAwait(false);

        return StampOf(path);
    }

    // ------------------------------------------------------------------ watching

    private void StartWatching(MarkdownDocument document)
    {
        if (document.Path is not { } path)
        {
            return;
        }

        StopWatching(document.Id);

        IFileWatcher watcher = _watcherFactory.Create();
        Guid id = document.Id;

        watcher.FileChanged += (_, changedPath) => OnFileChanged(id, changedPath);
        watcher.FileRemoved += (_, removedPath) => OnFileRemoved(id, removedPath);
        watcher.Watch(path);

        _watchers[id] = watcher;
    }

    private void StopWatching(Guid id)
    {
        if (_watchers.Remove(id, out IFileWatcher? watcher))
        {
            watcher.Dispose();
        }
    }

    private void OnFileChanged(Guid id, string path)
    {
        if (_suppressWatchUntil.TryGetValue(id, out DateTimeOffset until) && DateTimeOffset.UtcNow < until)
        {
            return;
        }

        if (Find(id) is not { } document)
        {
            return;
        }

        // A watcher fires on last-write time, name and size, so a touch, an antivirus scan or
        // a tool restamping the file all arrive here looking like edits. Asking about those is
        // what makes this whole feature something people switch off.
        if (!HasRealChange(document, path))
        {
            RecordStamp(id, StampOf(path));

            // A file that was missing and has come back holding exactly what we last read
            // from it needs its marker taken down. Nothing to ask about, but the tab is
            // still wearing a warning that has stopped being true.
            if (document.External == ExternalState.Missing)
            {
                MarkExternal(id, ExternalState.InSync);
            }

            return;
        }

        // Silently reloading over unsaved edits would destroy the user's work, so a dirty
        // buffer turns an external change into a prompt instead.
        if (document.IsDirty || !_settings.Current.ReloadOnExternalChange)
        {
            _logger.LogInformation("External change to {Path} needs the user to decide.", path);
            MarkExternal(id, ExternalState.Changed);
            return;
        }

        _ = ReloadSafelyAsync(id, path);
    }

    /// <summary>
    /// Whether the file on disk actually holds something this document does not.
    ///
    /// The stamp is only a cheap first pass: it can say "definitely nothing new" but never
    /// "definitely something new", because an editor that rewrites a file byte for byte still
    /// moves its timestamp. The content decides, and only a file too big to be worth reading
    /// on a watcher thread is judged on the stamp alone.
    ///
    /// An unreadable file counts as changed. It is usually mid-write by another process, and
    /// the caller's own error handling is a better place to deal with that than a guess here.
    /// </summary>
    private bool HasRealChange(MarkdownDocument document, string path)
    {
        try
        {
            FileStamp? current = StampOf(path);

            if (current is null)
            {
                return true;
            }

            if (document.Stamp == current)
            {
                return false;
            }

            if (current.Value.Length > MaxCompareBytes)
            {
                return true;
            }

            (string text, _, _) = ReadAllText(path);

            return !string.Equals(text, document.SavedText, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not compare {Path} against the buffer; treating it as changed.", path);
            return true;
        }
    }

    private async Task ReloadSafelyAsync(Guid id, string path)
    {
        try
        {
            await ReloadCoreAsync(id, automatic: true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Common when the writing process still holds the file; the next event retries.
            _logger.LogWarning(ex, "Automatic reload of {Path} failed.", path);
        }
    }

    private void OnFileRemoved(Guid id, string path)
    {
        // The buffer is kept so the user can save it back. Marking it missing is what makes
        // the document count as unsaved, which is what puts Ctrl+S back within reach.
        _logger.LogWarning("{Path} was deleted or moved while open.", path);
        MarkExternal(id, ExternalState.Missing);
    }

    /// <summary>Moves a document to a new external state, announcing it only if it moved.</summary>
    private void MarkExternal(Guid id, ExternalState state)
    {
        int index = _documents.FindIndex(d => d.Id == id);

        if (index < 0 || _documents[index].External == state)
        {
            return;
        }

        _documents[index] = _documents[index] with { External = state };
        Raise(WorkspaceChange.ExternalStateChanged, _documents[index]);
    }

    /// <summary>
    /// Notes what the file looks like now without disturbing anything else, so a run of events
    /// that change nothing is only paid for once.
    /// </summary>
    private void RecordStamp(Guid id, FileStamp? stamp)
    {
        int index = _documents.FindIndex(d => d.Id == id);

        if (index >= 0)
        {
            _documents[index] = _documents[index] with { Stamp = stamp };
        }
    }

    private void Raise(WorkspaceChange change, MarkdownDocument document) =>
        Changed?.Invoke(this, new WorkspaceChangedEventArgs(change, document, document.Id));

    public void Dispose()
    {
        foreach (IFileWatcher watcher in _watchers.Values)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }
}
