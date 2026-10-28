// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli.Outputs;

/// <summary>
/// Replacing what is in a folder, under --overwrite (amendment A5c, which reverses A5b).
///
/// Some folders are never emptied, whatever was asked: a drive root, the profile folder, the
/// current folder or one that holds it, and a folder holding anything the run reads - an input
/// document, or a picture it collects. Each is a slip of the keyboard away from a folder someone
/// meant to share from, and none could be put back.
///
/// The rest is replaced in a way that never leaves nothing: the new contents are written to a
/// sibling first, the old folder is moved aside, the new one is moved into its name, and only then
/// is the old one deleted. A run that fails or is stopped before the swap leaves the folder as it
/// was; one that fails during it puts the old folder back.
/// </summary>
internal static class FolderReplacement
{
    /// <summary>Why <paramref name="folder"/> may not be emptied, in a sentence, or null when it may.</summary>
    /// <param name="folder">The full path of the folder to replace.</param>
    /// <param name="reads">The full path of every file the run reads.</param>
    /// <param name="currentDirectory">The folder the run was started in.</param>
    public static string? Refusal(string folder, IEnumerable<string> reads, string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(reads);

        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

        if (Path.GetPathRoot(target) is { } root && string.Equals(target, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
        {
            return "It is a drive root, and that is never emptied, even with --overwrite.";
        }

        string profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        if (profile.Length > 0 && Holds(target, profile))
        {
            return "It is your profile folder or holds it, and that is never emptied, even with --overwrite.";
        }

        if (Holds(target, currentDirectory))
        {
            return "It is the current folder or holds it, and that is never emptied, even with --overwrite.";
        }

        if (reads.FirstOrDefault(read => Holds(target, read)) is { } input)
        {
            return $"It holds {input}, which this run reads, and a folder holding what is read is never emptied, even with --overwrite.";
        }

        return null;
    }

    /// <summary>
    /// Writes a folder with <paramref name="fill"/> and puts it in <paramref name="target"/>'s place,
    /// replacing whatever is there when <paramref name="overwrite"/> is set. Returns the old folder's
    /// path if it was moved aside but could not be deleted, so the run can say where it was left;
    /// null when nothing was left behind.
    /// </summary>
    public static async Task<string?> ReplaceAsync(string target, bool overwrite, Func<string, Task> fill)
    {
        ArgumentNullException.ThrowIfNull(fill);

        target = Path.TrimEndingDirectorySeparator(target);
        string parent = Path.GetDirectoryName(target)!;
        string stem = $".{Path.GetFileName(target)}.{Guid.NewGuid():n}";
        string pending = Path.Combine(parent, stem + ".tmp");
        string aside = Path.Combine(parent, stem + ".old");

        Directory.CreateDirectory(parent);

        try
        {
            await fill(pending).ConfigureAwait(false);

            if (!Directory.Exists(target))
            {
                Directory.Move(pending, target);
                return null;
            }

            // Without --overwrite the folder was empty when it was checked. It is removed without
            // recursion, so if anything has arrived in it since, the removal fails rather than
            // taking it away.
            if (!overwrite)
            {
                Directory.Delete(target, recursive: false);
                Directory.Move(pending, target);
                return null;
            }

            Directory.Move(target, aside);

            try
            {
                Directory.Move(pending, target);
            }
            catch
            {
                Directory.Move(aside, target);
                throw;
            }

            try
            {
                Directory.Delete(aside, recursive: true);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return aside;
            }
        }
        finally
        {
            if (Directory.Exists(pending))
            {
                Directory.Delete(pending, recursive: true);
            }
        }
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or somewhere under it.</summary>
    private static bool Holds(string folder, string path)
    {
        string inner = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        return string.Equals(inner, folder, StringComparison.OrdinalIgnoreCase)
            || inner.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
