// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>
/// Finds the repository a folder is in and reads its <c>.gitignore</c> files, top down.
///
/// A root is often a repository's <c>docs</c> folder, so the rules above the root count too: the
/// top's <c>.gitignore</c> is read even though the tree never shows that folder. Reading is a
/// local file read; this never writes anything and never runs git.
/// </summary>
public sealed class GitIgnoreReader
{
    private readonly Dictionary<string, GitIgnoreRules> _byFolder = new(StringComparer.OrdinalIgnoreCase);

    private GitIgnoreReader(string? repositoryTop) => RepositoryTop = repositoryTop;

    /// <summary>The folder holding <c>.git</c>, or null when the root is not in a repository.</summary>
    public string? RepositoryTop { get; }

    /// <summary>A reader for the repository holding <paramref name="root"/>, if there is one.</summary>
    public static GitIgnoreReader For(string root) => new(FindRepositoryTop(root));

    public static string? FindRepositoryTop(string folder)
    {
        try
        {
            for (DirectoryInfo? dir = new(folder); dir is not null; dir = dir.Parent)
            {
                string git = Path.Combine(dir.FullName, ".git");

                // A worktree or a submodule has a .git file pointing elsewhere; it is still the top.
                if (Directory.Exists(git) || File.Exists(git))
                {
                    return dir.FullName;
                }
            }
        }
        catch (Exception ex) when (FolderListing.IsReadFailure(ex))
        {
            return null;
        }

        return null;
    }

    /// <summary>Whether a path below the repository's top is ignored. Always false outside a repository.</summary>
    public bool IsIgnored(string fullPath, bool isDirectory)
    {
        if (RepositoryTop is null || Path.GetDirectoryName(fullPath) is not { } parent)
        {
            return false;
        }

        string? relative = RelativeToTop(fullPath);

        return relative is not null && RulesFor(parent).IsIgnored(relative, isDirectory);
    }

    /// <summary>Forgets what was read for a folder and everything under it, after a .gitignore changed.</summary>
    public void Forget(string folder)
    {
        foreach (string key in _byFolder.Keys.Where(k => FolderPaths.IsSameOrUnder(k, folder)).ToList())
        {
            _byFolder.Remove(key);
        }
    }

    /// <summary>The rules in force inside a folder: its parent's, followed by its own file's.</summary>
    private GitIgnoreRules RulesFor(string folder)
    {
        if (_byFolder.TryGetValue(folder, out GitIgnoreRules? known))
        {
            return known;
        }

        GitIgnoreRules inherited = FolderPaths.Equal(folder, RepositoryTop!)
            || Path.GetDirectoryName(folder) is not { } parent
            || RelativeToTop(folder) is null
            ? GitIgnoreRules.Empty
            : RulesFor(parent);

        GitIgnoreRules rules = inherited;

        if (RelativeToTop(folder) is { } relative)
        {
            string file = Path.Combine(folder, ".gitignore");

            try
            {
                if (File.Exists(file))
                {
                    rules = inherited.With(relative, File.ReadAllText(file));
                }
            }
            catch (Exception ex) when (FolderListing.IsReadFailure(ex))
            {
                // An unreadable .gitignore grays nothing.
            }
        }

        _byFolder[folder] = rules;
        return rules;
    }

    /// <summary>The path relative to the repository's top with '/' separators, "" for the top, null outside.</summary>
    private string? RelativeToTop(string fullPath)
    {
        if (FolderPaths.Equal(fullPath, RepositoryTop!))
        {
            return "";
        }

        return FolderPaths.IsSameOrUnder(fullPath, RepositoryTop!)
            ? Path.GetRelativePath(RepositoryTop!, fullPath).Replace('\\', '/')
            : null;
    }
}
