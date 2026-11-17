// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>
/// Comparing full paths the way Windows does: ignoring case, and treating a folder as a prefix
/// only at a separator, so <c>notes</c> is not a parent of <c>notes-old\a.md</c>.
/// </summary>
public static class FolderPaths
{
    public static bool Equal(string a, string b) =>
        string.Equals(TrimEnd(a), TrimEnd(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> itself or anything below it.</summary>
    public static bool IsSameOrUnder(string path, string folder)
    {
        string p = TrimEnd(path);
        string f = TrimEnd(folder);

        if (!p.StartsWith(f, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return p.Length == f.Length || IsSeparator(p[f.Length]) || (f.Length > 0 && IsSeparator(f[^1]));
    }

    /// <summary>Whether <paramref name="path"/> is strictly below <paramref name="folder"/>.</summary>
    public static bool IsUnder(string path, string folder) =>
        IsSameOrUnder(path, folder) && !Equal(path, folder);

    /// <summary>
    /// <paramref name="path"/> with its <paramref name="from"/> prefix replaced by
    /// <paramref name="to"/>. The path must be <paramref name="from"/> or below it.
    /// </summary>
    public static string Rebase(string path, string from, string to)
    {
        if (!IsSameOrUnder(path, from))
        {
            throw new ArgumentException($"'{path}' is not under '{from}'.", nameof(path));
        }

        string rest = TrimEnd(path)[TrimEnd(from).Length..].TrimStart('\\', '/');
        return rest.Length == 0 ? TrimEnd(to) : Path.Combine(TrimEnd(to), rest);
    }

    /// <summary>
    /// A path without a trailing separator, except a drive root, which keeps it: <c>C:</c> on its
    /// own means "the current folder on C", not the root.
    /// </summary>
    public static string TrimEnd(string path)
    {
        string trimmed = path.TrimEnd('\\', '/');

        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : trimmed;
    }

    private static bool IsSeparator(char c) => c is '\\' or '/';
}
