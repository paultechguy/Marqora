// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// The snippets available to insert: a curated set that ships with the app, plus whatever
/// the user has dropped into their snippets folder.
/// </summary>
public interface ISnippetCatalog
{
    /// <summary>
    /// Everything in a group, built-ins first and then the user's, each sorted by name.
    ///
    /// Only names are gathered here; no file is opened. The menu is rebuilt every time it
    /// opens, so this has to stay cheap enough to run on the UI thread.
    /// </summary>
    IReadOnlyList<Snippet> List(SnippetGroup group);

    /// <summary>
    /// The text to insert, read at the moment it is needed.
    ///
    /// Reading late rather than caching means a snippet edited in another editor is always
    /// current, which is also why the catalogue needs no file watcher. Returns null when
    /// the file has gone or cannot be read.
    /// </summary>
    Task<string?> ReadBodyAsync(Snippet snippet, CancellationToken cancellationToken = default);
}
