// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// The built-in snippets, plus whatever is in the user's snippets folder.
///
/// Nothing is cached and no folder is watched. The list is gathered from filenames each
/// time a menu opens — a directory listing, no file opened — and a body is read only when
/// one is actually inserted. That keeps a snippet edited elsewhere always current, and it
/// spares the app a background watcher, its debounce, and the way one quietly dies when its
/// directory is deleted and recreated.
/// </summary>
public sealed partial class SnippetCatalog(IAppPaths paths, ILogger<SnippetCatalog> logger) : ISnippetCatalog
{
    /// <summary>Beyond this a file is not a snippet, whatever it is.</summary>
    private const long MaxSnippetBytes = 256 * 1024;

    private static readonly string[] Extensions = [".md", ".markdown", ".txt"];

    public IReadOnlyList<Snippet> List(SnippetGroup group)
    {
        List<Snippet> builtIn = [.. BuiltInSnippets.All.Where(s => s.Group == group)];

        // The user's folder holds general snippets. Diagrams stay curated: they exist to
        // teach mermaid's syntax, and a half-remembered one would not.
        if (group != SnippetGroup.General)
        {
            return builtIn;
        }

        List<Snippet> user = ReadUserSnippets();

        if (user.Count == 0)
        {
            return builtIn;
        }

        // A user's snippet shadows a built-in of the same name. That is the natural way to
        // say "not that one, mine".
        HashSet<string> overridden = new(user.Select(s => s.Name), StringComparer.CurrentCultureIgnoreCase);

        return
        [
            .. builtIn.Where(s => !overridden.Contains(s.Name)),
            .. user,
        ];
    }

    public async Task<string?> ReadBodyAsync(Snippet snippet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snippet);

        if (snippet.Body is { } body)
        {
            return body;
        }

        if (snippet.Path is not { } path)
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Deleted or locked since the menu was built. Nothing to insert, nothing worth
            // interrupting the user over.
            logger.LogWarning(ex, "Could not read snippet {Path}.", path);

            return null;
        }
    }

    private List<Snippet> ReadUserSnippets()
    {
        string folder = paths.SnippetsDirectory;

        try
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }

            List<Snippet> found = [];

            foreach (string path in Directory.EnumerateFiles(folder))
            {
                if (!Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (new FileInfo(path).Length > MaxSnippetBytes)
                {
                    logger.LogInformation("Skipping {Path}; too large to be a snippet.", path);

                    continue;
                }

                string name = DisplayName(Path.GetFileNameWithoutExtension(path));

                if (name.Length > 0)
                {
                    found.Add(new Snippet { Name = name, Group = SnippetGroup.General, Path = path });
                }
            }

            // Sorted by the name as shown, so a numeric prefix on the file orders the menu
            // without appearing in it. Culture-aware on purpose: these are names a person
            // reads, not keys being matched.
            found.Sort(static (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));

            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not read the snippets folder {Folder}.", folder);

            return [];
        }
    }

    /// <summary>
    /// Turns a filename into a menu entry: "10-front-matter.md" becomes "front matter".
    ///
    /// The leading number is a sorting device, so it is stripped from what is shown while
    /// still deciding the order. Casing is left exactly as written — title-casing would
    /// turn "API notes" into "Api Notes", and the user's own name for their own snippet
    /// should win.
    /// </summary>
    private static string DisplayName(string fileName) =>
        Whitespace().Replace(SortPrefix().Replace(fileName, string.Empty).Replace('-', ' ').Replace('_', ' '), " ")
            .Trim();

    [GeneratedRegex(@"^\d+[-_. ]+")]
    private static partial Regex SortPrefix();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Whitespace();
}
