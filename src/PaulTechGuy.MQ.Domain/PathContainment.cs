// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Whether one path is inside another, decided the same way everywhere.
///
/// The app asks this question in five places - before writing a pasted image, before serving a
/// file to the preview, before embedding one in an export, before reporting a link as dead, and
/// before putting one in a Folio - and every one of them is a place where getting it wrong hands
/// out a file that was never meant to leave its folder.
///
/// The trailing separator is the whole trick. Without it "C:\docs2\logo.png" starts with
/// "C:\docs" and reads as being inside it. Appending a separator to the candidate as well keeps
/// the folder itself contained, which is what a reference written as "." resolves to.
/// </summary>
public static class PathContainment
{
    /// <summary>
    /// Whether <paramref name="candidate"/> is <paramref name="folder"/> or something beneath it.
    /// Both are resolved to full paths first, so "..\" in either is answered rather than trusted.
    /// </summary>
    /// <returns>
    /// False for a path this filesystem cannot express. A path that cannot be resolved is not
    /// contained by anything, and saying so beats throwing at every call site.
    /// </returns>
    public static bool Contains(string folder, string candidate)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            string root = Path.GetFullPath(folder + Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(candidate);

            return (full + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// The full path a relative reference resolves to inside <paramref name="folder"/>, or null
    /// when it escapes, or is absolute, or is not a path this filesystem can express.
    ///
    /// Forward slashes are converted on the way in, because a markdown reference is written with
    /// them whatever the platform underneath.
    /// </summary>
    public static string? ResolveWithin(string folder, string relative)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(relative))
        {
            return null;
        }

        try
        {
            string full = Path.GetFullPath(
                Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar)));

            return Contains(folder, full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
