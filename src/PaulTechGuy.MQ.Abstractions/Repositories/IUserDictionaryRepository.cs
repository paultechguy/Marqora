// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Repositories;

/// <summary>
/// Reads and writes the user's word list. Never throws for missing or unreadable storage.
///
/// The file is plain text, one word per line, and is meant to be edited by hand as well as by
/// the app - so reading is forgiving and writing is tidy. See the implementation for exactly
/// what that means on each side.
/// </summary>
public interface IUserDictionaryRepository
{
    /// <summary>
    /// Every word in the file, in the order it happens to be in. An empty list means there is
    /// no file yet, which is the ordinary state on a fresh install.
    /// </summary>
    Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the file with <paramref name="words"/>, sorted and de-duplicated.
    /// </summary>
    Task SaveAsync(IReadOnlyCollection<string> words, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a list from a file the user chose, by exactly the rules <see cref="LoadAsync"/> uses.
    ///
    /// Unlike LoadAsync this does not swallow a failure: the user named this file, so being told
    /// it could not be read is the useful answer rather than a silent empty list.
    /// </summary>
    Task<IReadOnlyList<string>> ImportFromAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a list to a file the user chose, formatted exactly as <see cref="SaveAsync"/> writes
    /// the app's own - so an exported list and the real one are the same kind of file.
    /// </summary>
    Task ExportToAsync(string path, IReadOnlyCollection<string> words, CancellationToken cancellationToken = default);
}
