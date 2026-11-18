// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>
/// Makes command-line paths absolute in the process that was given them (P9).
///
/// A second launch hands its arguments to the copy of Marqora already running, and that copy has
/// a different working folder. A relative path, <c>notes.md</c> or <c>.</c>, means something only
/// in the folder it was typed in, so it is made absolute before the handoff, and the receiver
/// refuses anything that is not.
/// </summary>
public static class LaunchArguments
{
    /// <summary>A switch, not a path. The same test the receiver has always used.</summary>
    public static bool IsSwitch(string arg) => arg.StartsWith('-') || arg.StartsWith('/');

    /// <summary>Whether any argument is a path that is not already absolute.</summary>
    public static bool AnyRelative(IEnumerable<string> args) =>
        args.Any(a => !string.IsNullOrWhiteSpace(a) && !IsSwitch(a) && !Path.IsPathFullyQualified(a));

    /// <summary>
    /// Every path made absolute against <paramref name="currentDirectory"/>; switches and blanks
    /// as they were. An argument that is not a legal path is left as it was, for the receiver to
    /// refuse.
    /// </summary>
    public static IReadOnlyList<string> Qualify(IEnumerable<string> args, string currentDirectory)
    {
        var qualified = new List<string>();

        foreach (string arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg) || IsSwitch(arg) || Path.IsPathFullyQualified(arg))
            {
                qualified.Add(arg);
                continue;
            }

            try
            {
                qualified.Add(FolderPaths.TrimEnd(Path.GetFullPath(arg, currentDirectory)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                qualified.Add(arg);
            }
        }

        return qualified;
    }

    /// <summary>What the receiving instance accepts: a fully qualified path, never one relative to its own folder.</summary>
    public static bool IsAcceptable(string arg) =>
        !string.IsNullOrWhiteSpace(arg) && !IsSwitch(arg) && Path.IsPathFullyQualified(arg);
}
