// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Repositories;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Spelling;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// The words this user has accepted, held in memory and written through to the file.
///
/// <see cref="Contains"/> is asked once per misspelled word per pass, on the thread pool, so it
/// answers from a set rather than from disk. Everything that changes the set replaces it rather
/// than mutating it, which is what lets a reader hold one without a lock.
///
/// The file is watched because it is meant to be edited by hand: a word list you can open in
/// Marqora, correct, and save is one of the reasons it is plain text at all, and a list that only
/// noticed changes on restart would make that a half-feature.
/// </summary>
public sealed class UserDictionaryService : IUserDictionary, IDisposable
{
    private readonly IUserDictionaryRepository _repository;
    private readonly IAppPaths _paths;
    private readonly ILogger<UserDictionaryService> _logger;
    private readonly IFileWatcher _watcher;
    private readonly Lock _sync = new();

    /// <summary>
    /// Ordinal-ignore-case: a word at the start of a sentence is the same word, and the answer
    /// must not depend on the machine's culture.
    /// </summary>
    private HashSet<string> _words = new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public UserDictionaryService(
        IUserDictionaryRepository repository,
        IAppPaths paths,
        IFileWatcherFactory watchers,
        ILogger<UserDictionaryService> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(watchers);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _paths = paths;
        _logger = logger;

        _watcher = watchers.Create();
        _watcher.FileChanged += OnFileChanged;
    }

    public IReadOnlyCollection<string> Words
    {
        get
        {
            lock (_sync)
            {
                return _words;
            }
        }
    }

    public event EventHandler? Changed;

    public bool Contains(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        lock (_sync)
        {
            return _words.Contains(word);
        }
    }

    /// <summary>
    /// Reads the file and starts watching it. Safe to call more than once.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await ReloadAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Watched whether or not it exists yet: a first Add creates it, and the watcher
            // follows the path rather than the file.
            _watcher.Watch(_paths.UserDictionaryPath);
        }
        catch (Exception ex)
        {
            // Not fatal. The list still works; it just will not notice a hand edit until the
            // next launch.
            _logger.LogWarning(ex, "Could not watch the user dictionary for changes.");
        }
    }

    public async Task AddAsync(string word, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(word);

        string trimmed = word.Trim();

        if (trimmed.Length == 0)
        {
            return;
        }

        string[] snapshot;

        lock (_sync)
        {
            if (_words.Contains(trimmed))
            {
                // Already known. Not an error, and not worth rewriting the file for.
                return;
            }

            var updated = new HashSet<string>(_words, StringComparer.OrdinalIgnoreCase) { trimmed };

            _words = updated;
            snapshot = [.. updated];
        }

        // Announced before the write finishes. The squiggles clear from what is already in
        // memory, and the file catching up a few milliseconds later changes nothing anyone sees.
        Changed?.Invoke(this, EventArgs.Empty);

        await _repository.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Added \"{Word}\" to the dictionary; it now holds {Count}.", trimmed, snapshot.Length);
    }

    /// <summary>
    /// Adds several words at once, for an import. Reports how many were genuinely new.
    /// </summary>
    public async Task<int> AddRangeAsync(IEnumerable<string> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);

        string[] snapshot;
        int added;

        lock (_sync)
        {
            var updated = new HashSet<string>(_words, StringComparer.OrdinalIgnoreCase);

            foreach (string word in words)
            {
                string trimmed = word?.Trim() ?? string.Empty;

                if (trimmed.Length > 0)
                {
                    updated.Add(trimmed);
                }
            }

            added = updated.Count - _words.Count;

            if (added == 0)
            {
                return 0;
            }

            _words = updated;
            snapshot = [.. updated];
        }

        Changed?.Invoke(this, EventArgs.Empty);

        await _repository.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);

        return added;
    }

    /// <summary>Re-reads the file, replacing what is held.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> loaded = await _repository.LoadAsync(cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _words = new HashSet<string>(loaded, StringComparer.OrdinalIgnoreCase);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The file changed under us - almost always because it was opened in Marqora and saved.
    /// </summary>
    private async void OnFileChanged(object? sender, string path)
    {
        try
        {
            await ReloadAsync().ConfigureAwait(false);

            _logger.LogInformation("The user dictionary was edited outside the app; reloaded {Count} word(s).", Words.Count);
        }
        catch (Exception ex)
        {
            // An async void handler: nothing above it can catch, so it catches everything.
            _logger.LogWarning(ex, "Could not reload the user dictionary after it changed on disk.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _watcher.FileChanged -= OnFileChanged;
        _watcher.Dispose();
    }
}
