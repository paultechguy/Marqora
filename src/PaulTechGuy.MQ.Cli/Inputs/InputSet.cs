// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.FileSystemGlobbing;

namespace PaulTechGuy.MQ.Cli.Inputs;

/// <summary>One input: a file, or standard input when <see cref="FullPath"/> is null.</summary>
/// <param name="Display">How messages and JSON name it: as typed when it was typed, relative to the current folder when a pattern found it, "-" for standard input.</param>
internal sealed record InputFile(string Display, string? FullPath)
{
    public bool IsStandardInput => FullPath is null;
}

/// <summary>A name or pattern that found no file, and why, in a sentence.</summary>
internal sealed record InputProblem(string Given, string Message);

/// <summary>The order a run's inputs come back in.</summary>
internal enum InputOrder
{
    /// <summary>Ordinal order of the full path: the same tree is always visited the same way.</summary>
    Sorted,

    /// <summary>
    /// The order they were named in, each pattern's matches in ordinal order where the pattern
    /// stands. For folio, whose order is the reading order (amendment A15).
    /// </summary>
    AsNamed,
}

/// <summary>
/// The inputs a run was given, made into files (CLI-IN-01, CLI-IN-02).
///
/// Patterns are expanded here rather than by a shell, because PowerShell and cmd do not expand
/// them and bash does it differently: "docs/**/*.md" must mean one set of files wherever it is
/// typed. Matches come back once each - a file named twice stays where it was first named - and
/// by default in ordinal order of their full path, so a run over the same tree always visits files
/// in the same order. Names are compared ignoring case, as Windows does.
/// </summary>
internal static class InputSet
{
    private static readonly char[] Wildcards = ['*', '?', '['];

    public static (IReadOnlyList<InputFile> Files, IReadOnlyList<InputProblem> Problems) Resolve(
        IEnumerable<string> given,
        string currentDirectory,
        InputOrder order = InputOrder.Sorted)
    {
        ArgumentNullException.ThrowIfNull(given);

        var files = new InputList();
        var problems = new List<InputProblem>();
        InputFile? standardInput = null;

        foreach (string item in given)
        {
            if (item == "-")
            {
                standardInput = new InputFile("-", null);
                continue;
            }

            if (item.IndexOfAny(Wildcards) >= 0)
            {
                Expand(item, currentDirectory, files, problems);
                continue;
            }

            string full = Path.GetFullPath(item, currentDirectory);

            if (File.Exists(full))
            {
                files.Add(new InputFile(item, full));
            }
            else if (Directory.Exists(full))
            {
                problems.Add(new InputProblem(item, $"is a folder. Give the files in it with a pattern, such as \"{item.TrimEnd('/', '\\')}/**/*.md\"."));
            }
            else
            {
                problems.Add(new InputProblem(item, "No such file."));
            }
        }

        if (standardInput is not null)
        {
            return ([standardInput], problems);
        }

        return (order == InputOrder.AsNamed ? files.InOrder : [.. files.InOrder.OrderBy(file => file.FullPath, StringComparer.Ordinal)], problems);
    }

    /// <summary>
    /// A pattern, split where its wildcards begin: the folder written before them is where the
    /// search starts, and the rest is matched inside it. "docs/**/*.md" searches docs for **/*.md;
    /// "*.md" searches the current folder.
    /// </summary>
    private static void Expand(string pattern, string currentDirectory, InputList files, List<InputProblem> problems)
    {
        string[] segments = pattern.Split(['/', '\\']);
        int firstWild = Array.FindIndex(segments, segment => segment.IndexOfAny(Wildcards) >= 0);

        string literal = string.Join(Path.DirectorySeparatorChar, segments[..firstWild]);
        string rest = string.Join('/', segments[firstWild..]);

        // A pattern starting at a drive root keeps its separator: "C:" alone would mean the
        // current folder on that drive.
        if (literal.EndsWith(':'))
        {
            literal += Path.DirectorySeparatorChar;
        }

        string root = Path.GetFullPath(literal.Length == 0 ? "." : literal, currentDirectory);

        if (!Directory.Exists(root))
        {
            problems.Add(new InputProblem(pattern, $"No file matches the pattern: {root} does not exist."));
            return;
        }

        var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
        matcher.AddInclude(rest);

        bool any = false;

        foreach (string full in matcher.GetResultsInFullPath(root).Order(StringComparer.Ordinal))
        {
            any = true;
            string normalized = Path.GetFullPath(full);

            files.Add(new InputFile(Path.GetRelativePath(currentDirectory, normalized), normalized));
        }

        if (!any)
        {
            problems.Add(new InputProblem(pattern, "No file matches the pattern."));
        }
    }

    /// <summary>The files found so far, once each, in the order they were found.</summary>
    private sealed class InputList
    {
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<InputFile> _files = [];

        public IReadOnlyList<InputFile> InOrder => _files;

        public void Add(InputFile file)
        {
            if (_seen.Add(file.FullPath!))
            {
                _files.Add(file);
            }
        }
    }
}
