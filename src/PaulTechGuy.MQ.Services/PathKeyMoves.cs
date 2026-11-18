// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Folders;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Moving keys in a set of normalized paths when a file or folder is renamed, shared by the
/// read-only marks and the pins for the reason <see cref="DocumentPathKeys"/> is shared: the two
/// stores are the same set twice over, and the subtle part should have one copy.
///
/// The subtle part is the two spellings (C3). <see cref="DocumentPathKeys.Normalize"/> resolves a
/// link only when the full path <em>is</em> one, so a file reached through a junction folder is
/// stored under the junction's spelling, while the junction itself normalizes to its target. A
/// folder prefix is therefore matched both ways.
/// </summary>
internal static class PathKeyMoves
{
    public static PreparedMove Prepare(string from, IEnumerable<string> keys)
    {
        string full = FullPath(from);
        string resolved = DocumentPathKeys.Normalize(from);

        List<string> matched =
        [
            .. keys.Where(k => FolderPaths.IsSameOrUnder(k, full) || FolderPaths.IsSameOrUnder(k, resolved)),
        ];

        return new PreparedMove(full, resolved, matched);
    }

    /// <summary>
    /// Swaps each prepared key's old prefix for the new path, in the set. Answers whether
    /// anything changed. A key already gone from the set (unmarked in the meantime) stays gone.
    /// </summary>
    public static bool Apply(HashSet<string> set, PreparedMove prepared, string to)
    {
        if (prepared.IsEmpty)
        {
            return false;
        }

        string toFull = FullPath(to);
        string toResolved = DocumentPathKeys.Normalize(to);
        bool changed = false;

        foreach (string key in prepared.Keys)
        {
            if (!set.Remove(key))
            {
                continue;
            }

            // A key under the spelling as written moves with that spelling; one stored under a
            // link's target moves with wherever that target now resolves (unchanged, when only
            // the link was renamed).
            string moved = FolderPaths.IsSameOrUnder(key, prepared.From)
                ? FolderPaths.Rebase(key, prepared.From, toFull)
                : FolderPaths.Rebase(key, prepared.FromResolved, toResolved);

            set.Add(DocumentPathKeys.Normalize(moved));
            changed = true;
        }

        return changed;
    }

    /// <summary>Whether any key sits at the folder or below it, under either spelling.</summary>
    public static bool AnyUnder(IEnumerable<string> keys, string folder)
    {
        string full = FullPath(folder);
        string resolved = DocumentPathKeys.Normalize(folder);

        return keys.Any(k => FolderPaths.IsSameOrUnder(k, full) || FolderPaths.IsSameOrUnder(k, resolved));
    }

    /// <summary>Removes every key at the path or below it, under either spelling. Answers whether any went.</summary>
    public static bool RemoveUnder(HashSet<string> set, string path)
    {
        string full = FullPath(path);
        string resolved = DocumentPathKeys.Normalize(path);

        return set.RemoveWhere(k => FolderPaths.IsSameOrUnder(k, full) || FolderPaths.IsSameOrUnder(k, resolved)) > 0;
    }

    private static string FullPath(string path)
    {
        try
        {
            return FolderPaths.TrimEnd(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
