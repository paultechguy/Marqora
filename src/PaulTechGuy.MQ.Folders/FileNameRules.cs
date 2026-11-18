// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Folders;

/// <summary>Why a new name cannot be used.</summary>
public enum NameProblem
{
    None,

    /// <summary>The name is the one it already has. The rename box just closes.</summary>
    Unchanged,
    Empty,
    IllegalCharacter,
    ReservedName,
    TrailingDotOrSpace,
    TooLong,
    AlreadyTaken,
}

/// <summary>The answer to "can this be renamed to that", as the rename box shows it.</summary>
/// <param name="Message">The reason, written for the person typing. Null when the name is fine.</param>
/// <param name="IsCaseOnly">Only the case changes, which Windows treats as the same name.</param>
/// <param name="LeavesTree">
/// A markdown file is being given an extension the tree does not list, so it will drop out of
/// sight unless Show all files is on. Not a problem; the panel asks before going ahead.
/// </param>
public sealed record NameCheck(NameProblem Problem, string? Message, bool IsCaseOnly, bool LeavesTree)
{
    public bool IsAcceptable => Problem == NameProblem.None;
}

/// <summary>
/// Whether a new name for a file or folder is one Windows will take, decided while the rename
/// box is still open, so the reason is shown where it was typed rather than after the box closes.
/// </summary>
public static class FileNameRules
{
    /// <summary>The longest single name NTFS allows.</summary>
    public const int MaxNameLength = 255;

    private static readonly char[] Illegal = Path.GetInvalidFileNameChars();

    private static readonly HashSet<string> ReservedNames = BuildReserved();

    /// <summary>
    /// Checks <paramref name="newName"/> as the new name for <paramref name="oldName"/> inside
    /// <paramref name="folder"/>.
    /// </summary>
    /// <param name="isFolder">Whether the thing being renamed is a folder.</param>
    /// <param name="showAllFiles">Whether the tree lists every file, so no file can leave it.</param>
    public static NameCheck Check(string folder, string oldName, string newName, bool isFolder, bool showAllFiles)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return Fail(NameProblem.Unchanged, null);
        }

        if (string.IsNullOrWhiteSpace(newName))
        {
            return Fail(NameProblem.Empty, "Type a name.");
        }

        int bad = newName.IndexOfAny(Illegal);

        if (bad >= 0)
        {
            string shown = char.IsControl(newName[bad]) ? "a control character" : $"\"{newName[bad]}\"";
            return Fail(NameProblem.IllegalCharacter, $"A name cannot contain {shown}.");
        }

        if (newName[^1] is '.' or ' ')
        {
            return Fail(NameProblem.TrailingDotOrSpace, "A name cannot end with a dot or a space.");
        }

        if (IsReserved(newName))
        {
            return Fail(NameProblem.ReservedName, $"\"{ReservedStem(newName)}\" is reserved by Windows.");
        }

        if (newName.Length > MaxNameLength)
        {
            return Fail(NameProblem.TooLong, $"A name can be at most {MaxNameLength} characters.");
        }

        bool caseOnly = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);

        if (!caseOnly && Exists(Path.Combine(folder, newName)))
        {
            return Fail(NameProblem.AlreadyTaken, $"\"{newName}\" already exists here.");
        }

        bool leaves = !isFolder
            && !showAllFiles
            && FolderListing.IsListedFile(oldName, showAllFiles: false)
            && !FolderListing.IsListedFile(newName, showAllFiles: false);

        return new NameCheck(NameProblem.None, null, caseOnly, leaves);
    }

    /// <summary>Whether Windows reserves the name, with or without an extension: CON, CON.txt, com1.md.</summary>
    public static bool IsReserved(string name) => ReservedNames.Contains(ReservedStem(name));

    private static string ReservedStem(string name)
    {
        int dot = name.IndexOf('.');
        return (dot < 0 ? name : name[..dot]).TrimEnd(' ');
    }

    private static bool Exists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception ex) when (FolderListing.IsReadFailure(ex))
        {
            return false;
        }
    }

    private static NameCheck Fail(NameProblem problem, string? message) => new(problem, message, false, false);

    private static HashSet<string> BuildReserved()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" };

        // COM and LPT run 1 to 9, and Windows reserves the superscript digits too.
        foreach (string digit in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "¹", "²", "³" })
        {
            names.Add("COM" + digit);
            names.Add("LPT" + digit);
        }

        return names;
    }
}
