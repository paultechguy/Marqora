// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Repositories;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// The user's word list, as a plain text file.
///
/// Not JSON, and not through <see cref="JsonFileStore{T}"/>, because this is the one file the app
/// writes that a person is expected to open. It can be committed to a repository as a project
/// glossary, reviewed in a diff, and edited in Marqora itself - none of which is true of a JSON
/// array with an envelope around it.
///
/// <b>Reading is forgiving, writing is tidy.</b> Anything a person might reasonably type is
/// accepted: either line ending, blank lines, stray indentation, and "#" comments so a shared
/// list can say why a word is in it. What the app writes back is sorted and de-duplicated, so two
/// machines that know the same words produce the same file and a diff shows only what changed.
///
/// Comments do not survive a write. The alternative - tracking which comment belonged to which
/// word so it could be put back - is a great deal of machinery for a file most people will never
/// open, and losing them is visible rather than silent.
///
/// Import and export go through the same reader and writer as the app's own file, so a list
/// shared between two machines is the same kind of file at both ends.
/// </summary>
public sealed class TextUserDictionaryRepository(IAppPaths paths, ILogger<TextUserDictionaryRepository> logger)
    : IUserDictionaryRepository, IDisposable
{
    private const char CommentMarker = '#';

    /// <summary>
    /// UTF-8 without a byte order mark. The list is mostly ASCII, a BOM would show up as stray
    /// characters at the top of every diff, and every reader this file is likely to meet copes
    /// without one.
    /// </summary>
    private static readonly UTF8Encoding FileEncoding = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default)
    {
        string path = paths.UserDictionaryPath;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            // An unreadable word list means no extra words, not a broken app. Logged rather than
            // quarantined the way a corrupt settings file is: there is nothing here the user
            // cannot retype, and moving their file out from under them would be the ruder answer.
            logger.LogWarning(ex, "Could not read the user dictionary at {Path}.", path);

            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<string> words, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);

        string path = paths.UserDictionaryPath;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteAsync(path, Tidy(words), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write the user dictionary to {Path}.", path);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Deliberately does not catch, unlike <see cref="LoadAsync"/>. The user named this file, so
    /// being told it could not be read is the useful answer; a silent empty list would look like
    /// a file with nothing in it.
    /// </summary>
    public async Task<IReadOnlyList<string>> ImportFromAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Also deliberately does not catch, for the reason <see cref="ImportFromAsync"/> gives.</summary>
    public async Task ExportToAsync(
        string path,
        IReadOnlyCollection<string> words,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(words);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await WriteAsync(path, Tidy(words), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------------ the file

    /// <summary>
    /// Every word in the file, in the order it holds them. A file that is not there yet reads as
    /// nothing, which is the ordinary state on a fresh install.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        string[] lines = await File.ReadAllLinesAsync(path, FileEncoding, cancellationToken)
            .ConfigureAwait(false);

        List<string> words = [];

        foreach (string line in lines)
        {
            string word = line.Trim();

            if (word.Length == 0 || word[0] == CommentMarker)
            {
                continue;
            }

            words.Add(word);
        }

        return words;
    }

    /// <summary>
    /// Written beside the target and moved into place, the same way <see cref="JsonFileStore{T}"/>
    /// does it, so a failure part-way through leaves the previous list intact rather than a
    /// half-written one.
    /// </summary>
    private static async Task WriteAsync(string path, string[] words, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string temporary = path + ".tmp";

        await File.WriteAllLinesAsync(temporary, words, FileEncoding, cancellationToken).ConfigureAwait(false);

        if (File.Exists(path))
        {
            File.Replace(temporary, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporary, path);
        }
    }

    /// <summary>
    /// Sorted and de-duplicated, so the file is the same whichever way the words arrived in it.
    ///
    /// Ordinal-ignore-case for both: "Marqora" and "marqora" are one word, and the ordering must
    /// not depend on the machine's culture, or two machines would disagree about the same list.
    /// </summary>
    private static string[] Tidy(IReadOnlyCollection<string> words) =>
        [.. words
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Select(word => word.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(word => word, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Releases the gate. Called by the DI container at shutdown.</summary>
    public void Dispose() => _gate.Dispose();
}
