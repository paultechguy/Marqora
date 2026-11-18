// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>
/// One visible row of the tree.
///
/// <see cref="PositionInSet"/> and <see cref="SizeOfSet"/> count visible siblings from one, and
/// with <see cref="Depth"/> are what Narrator reads to describe a flat list as a tree.
/// </summary>
public sealed record FolderRow(
    string FullPath,
    string Name,
    int Depth,
    FolderEntryKind Kind,
    bool IsExpanded,
    bool CanExpand,
    bool IsIgnored,
    int PositionInSet,
    int SizeOfSet)
{
    public bool IsFolder => Kind == FolderEntryKind.Folder;

    /// <summary>Images, other files and anything .gitignore names are drawn dimmed. Folders only when ignored.</summary>
    public bool IsDimmed => IsIgnored || Kind is FolderEntryKind.Image or FolderEntryKind.Other;
}

/// <summary>How the rows are built: what the filter box holds and how files are ordered.</summary>
public sealed record RowOptions(string Filter = "", bool SortByModified = false)
{
    public static RowOptions Default { get; } = new();

    public bool IsFiltering => !string.IsNullOrWhiteSpace(Filter);
}

/// <summary>A step that turns one row list into the next. Applied in order, they turn the old list into the new.</summary>
public abstract record RowChange;

public sealed record RowsRemoved(int Index, int Count) : RowChange;

public sealed record RowsInserted(int Index, IReadOnlyList<FolderRow> Rows) : RowChange;

/// <summary>The row at this index is the same path, with something about it changed (expanded, renamed in case, grayed).</summary>
public sealed record RowUpdated(int Index, FolderRow Row) : RowChange;

/// <summary>
/// Flattens the tree into the rows the list shows, and works out the smallest set of changes from
/// one row list to the next (P1).
///
/// The list is never replaced. Expanding a folder inserts a range; collapsing removes one; a
/// filter keystroke removes the rows that stopped matching and leaves the rest alone. Replacing
/// the list would lose the keyboard's place, the scroll position and Narrator's position, all at
/// once, on every keystroke.
/// </summary>
public static class FolderRows
{
    public static IReadOnlyList<FolderRow> Build(FolderTree tree, RowOptions options)
    {
        var rows = new List<FolderRow>();
        string? filter = options.IsFiltering ? options.Filter.Trim() : null;

        AddChildren(tree, tree.Root, depth: 0, options, filter, rows);
        return rows;
    }

    /// <summary>
    /// The changes that turn <paramref name="before"/> into <paramref name="after"/>, keyed by path.
    /// A row present in both is kept, and updated in place when anything about it differs. Rows
    /// that change places (a sort change) are removed and inserted, which is the honest answer.
    /// </summary>
    public static IReadOnlyList<RowChange> Diff(IReadOnlyList<FolderRow> before, IReadOnlyList<FolderRow> after)
    {
        var changes = new List<RowChange>();
        var oldKeys = new HashSet<string>(before.Select(r => r.FullPath), StringComparer.OrdinalIgnoreCase);
        var newKeys = new HashSet<string>(after.Select(r => r.FullPath), StringComparer.OrdinalIgnoreCase);

        int i = 0;
        int j = 0;

        // After each step, the working list's first j rows equal after[0..j).
        while (i < before.Count || j < after.Count)
        {
            if (i < before.Count && j < after.Count
                && string.Equals(before[i].FullPath, after[j].FullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (before[i] != after[j])
                {
                    changes.Add(new RowUpdated(j, after[j]));
                }

                i++;
                j++;
                continue;
            }

            if (i < before.Count && !newKeys.Contains(before[i].FullPath))
            {
                AddRemove(changes, j);
                i++;
                continue;
            }

            if (j < after.Count && !oldKeys.Contains(after[j].FullPath))
            {
                AddInsert(changes, j, after[j]);
                j++;
                continue;
            }

            if (i < before.Count)
            {
                // Present in both but out of place: take it out here; it goes back in where the
                // new list has it.
                oldKeys.Remove(before[i].FullPath);
                AddRemove(changes, j);
                i++;
                continue;
            }

            // Only reachable when the new list repeats a path, which Build never does.
            AddInsert(changes, j, after[j]);
            j++;
        }

        return changes;
    }

    /// <summary>Applies changes to a list, as the view does to its collection. Used by the tests to prove a diff.</summary>
    public static List<FolderRow> ApplyTo(IReadOnlyList<FolderRow> before, IReadOnlyList<RowChange> changes)
    {
        var rows = new List<FolderRow>(before);

        foreach (RowChange change in changes)
        {
            switch (change)
            {
                case RowsRemoved removed:
                    rows.RemoveRange(removed.Index, removed.Count);
                    break;
                case RowsInserted inserted:
                    rows.InsertRange(inserted.Index, inserted.Rows);
                    break;
                case RowUpdated updated:
                    rows[updated.Index] = updated.Row;
                    break;
            }
        }

        return rows;
    }

    private static void AddRemove(List<RowChange> changes, int index)
    {
        if (changes.Count > 0 && changes[^1] is RowsRemoved last && last.Index == index)
        {
            changes[^1] = last with { Count = last.Count + 1 };
            return;
        }

        changes.Add(new RowsRemoved(index, 1));
    }

    private static void AddInsert(List<RowChange> changes, int index, FolderRow row)
    {
        if (changes.Count > 0 && changes[^1] is RowsInserted last && last.Index + last.Rows.Count == index)
        {
            changes[^1] = new RowsInserted(last.Index, [.. last.Rows, row]);
            return;
        }

        changes.Add(new RowsInserted(index, [row]));
    }

    /// <summary>Adds a folder's visible children, and theirs where open. Answers whether anything was added.</summary>
    private static bool AddChildren(
        FolderTree tree,
        FolderNode folder,
        int depth,
        RowOptions options,
        string? filter,
        List<FolderRow> rows)
    {
        List<FolderNode> shown = [.. folder.Children.Where(tree.IsShown)];
        shown.Sort(Order(options));

        if (filter is not null)
        {
            shown = [.. shown.Where(c => Matches(c, filter) || HasMatchBelow(tree, c, filter))];
        }

        for (int k = 0; k < shown.Count; k++)
        {
            FolderNode child = shown[k];
            bool open;

            if (filter is null)
            {
                open = child.IsFolder && child.IsExpanded;
            }
            else
            {
                // While filtering, a folder is open exactly when something below it matches, so
                // every match shows where it lives.
                open = child.IsFolder && HasMatchBelow(tree, child, filter);
            }

            bool canExpand = child.IsFolder && (!child.IsLoaded || child.Children.Any(tree.IsShown));

            rows.Add(new FolderRow(
                child.FullPath,
                child.Name,
                depth,
                child.Kind,
                open && canExpand,
                canExpand,
                child.IsIgnored,
                k + 1,
                shown.Count));

            if (open)
            {
                AddChildren(tree, child, depth + 1, options, filter, rows);
            }
        }

        return shown.Count > 0;
    }

    private static bool Matches(FolderNode node, string filter) =>
        node.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase);

    private static bool HasMatchBelow(FolderTree tree, FolderNode folder, string filter) =>
        folder.IsFolder
        && folder.Children.Any(c => tree.IsShown(c) && (Matches(c, filter) || HasMatchBelow(tree, c, filter)));

    /// <summary>Folders first by name; then files by name, or newest first with ties by name (P19).</summary>
    private static Comparison<FolderNode> Order(RowOptions options) => (a, b) =>
    {
        if (a.IsFolder != b.IsFolder)
        {
            return a.IsFolder ? -1 : 1;
        }

        if (!a.IsFolder && options.SortByModified)
        {
            int byDate = b.LastWriteUtc.CompareTo(a.LastWriteUtc);

            if (byDate != 0)
            {
                return byDate;
            }
        }

        return NaturalNameComparer.Instance.Compare(a.Name, b.Name);
    };
}
