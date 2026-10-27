// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Cli.Tests.Harness;

namespace PaulTechGuy.MQ.Cli.Tests.Manual;

/// <summary>One fenced block of the manual: its info string, its lines, and the line its fence opens on.</summary>
internal sealed record FencedBlock(int Line, string Info, IReadOnlyList<string> Lines)
{
    public string Text => string.Join('\n', Lines);
}

/// <summary>An example: a command block, the comment before it if any, and the output block after it if any.</summary>
internal sealed record ManualExample(FencedBlock Command, string? Tag, FencedBlock? Output)
{
    public bool NoOutput => Tag == "no-output";

    /// <summary>What the example says it exits with: 0 unless an "exit N" tag says otherwise.</summary>
    public int ExitCode => Tag is { } tag && tag.StartsWith("exit ", StringComparison.Ordinal) ? int.Parse(tag[5..], System.Globalization.CultureInfo.InvariantCulture) : 0;
}

/// <summary>
/// A document of the CLI's, read the way its tests need it (CLI-DOC-01): its sections by heading,
/// its fenced blocks, and the examples among them. There are two, the manual and the recipes, held
/// to the same rules; the rules are in the comment at the top of each, so whoever edits one reads
/// them there.
/// </summary>
internal sealed partial class Manual
{
    public const string TourFolder = "docs/cli-tour";

    /// <summary>docs/CLI.md, the manual.</summary>
    public static readonly Manual Cli = new("docs/CLI.md");

    /// <summary>docs/CLI-Recipes.md, the tasks that combine commands with a shell.</summary>
    public static readonly Manual Recipes = new("docs/CLI-Recipes.md");

    public static IReadOnlyList<Manual> All => [Cli, Recipes];

    private readonly Lazy<string[]> _lines;

    private Manual(string relativePath)
    {
        RelativePath = relativePath;
        _lines = new(() => File.ReadAllText(Repo.At(relativePath)).ReplaceLineEndings("\n").Split('\n'));
    }

    public string RelativePath { get; }

    public IReadOnlyList<string> Lines => _lines.Value;

    public string Text => string.Join('\n', Lines);

    public static Manual Named(string relativePath) => All.Single(m => m.RelativePath == relativePath);

    /// <summary>
    /// The text under a heading, up to the next heading of the same level or higher. The heading is
    /// matched exactly, so "### `number`" is not "### `number-headings`".
    /// </summary>
    public string? Section(string heading)
    {
        int level = heading.TakeWhile(c => c == '#').Count();
        int start = Array.IndexOf(_lines.Value, heading);

        if (start < 0)
        {
            return null;
        }

        var lines = new List<string>();
        string? fence = null;

        foreach (string line in _lines.Value.Skip(start + 1))
        {
            // A "# comment" in a shell block is not a heading.
            if (Fence().Match(line) is { Success: true } match)
            {
                string marks = match.Groups["fence"].Value;

                if (fence is null)
                {
                    fence = marks;
                }
                else if (marks.Length >= fence.Length && match.Groups["info"].Value.Length == 0)
                {
                    fence = null;
                }
            }

            int depth = line.TakeWhile(c => c == '#').Count();

            if (fence is null && depth > 0 && depth <= level && line.Length > depth && line[depth] == ' ')
            {
                break;
            }

            lines.Add(line);
        }

        return string.Join('\n', lines);
    }

    /// <summary>Every fenced block, outside the HTML comment at the top; a fence closes only on one at least as long.</summary>
    public IReadOnlyList<FencedBlock> Blocks()
    {
        var blocks = new List<FencedBlock>();
        string[] lines = _lines.Value;
        bool inComment = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (!inComment && line.StartsWith("<!--", StringComparison.Ordinal) && !line.Contains("-->", StringComparison.Ordinal))
            {
                inComment = true;
                continue;
            }

            if (inComment)
            {
                inComment = !line.Contains("-->", StringComparison.Ordinal);
                continue;
            }

            Match open = Fence().Match(line);

            if (!open.Success)
            {
                continue;
            }

            string fence = open.Groups["fence"].Value;
            int start = i;
            var body = new List<string>();

            for (i++; i < lines.Length; i++)
            {
                Match close = Fence().Match(lines[i]);

                if (close.Success && close.Groups["fence"].Value.Length >= fence.Length && close.Groups["info"].Value.Length == 0)
                {
                    break;
                }

                body.Add(lines[i]);
            }

            blocks.Add(new FencedBlock(start + 1, open.Groups["info"].Value, body));
        }

        return blocks;
    }

    /// <summary>The "cli-example" comment on the line before a block, ignoring blank lines, or null.</summary>
    public string? TagBefore(FencedBlock block)
    {
        for (int i = block.Line - 2; i >= 0; i--)
        {
            string line = _lines.Value[i].Trim();

            if (line.Length == 0)
            {
                continue;
            }

            Match tag = Tag().Match(line);

            return tag.Success ? tag.Groups["tag"].Value : null;
        }

        return null;
    }

    public static bool IsExample(FencedBlock block) =>
        block.Lines.Count > 0 && block.Lines[0].StartsWith("marqora-cli ", StringComparison.Ordinal);

    /// <summary>Every example: a command block, and the output block that follows it unless it is tagged no-output.</summary>
    public IReadOnlyList<ManualExample> Examples()
    {
        IReadOnlyList<FencedBlock> blocks = Blocks();
        var examples = new List<ManualExample>();

        for (int i = 0; i < blocks.Count; i++)
        {
            if (!IsExample(blocks[i]))
            {
                continue;
            }

            string? tag = TagBefore(blocks[i]);
            FencedBlock? output = tag != "no-output" && i + 1 < blocks.Count && blocks[i + 1].Info == "text" && TagBefore(blocks[i + 1]) is null
                ? blocks[i + 1]
                : null;

            examples.Add(new ManualExample(blocks[i], tag, output));
        }

        return examples;
    }

    public override string ToString() => RelativePath;

    /// <summary>
    /// A command line made into arguments as a shell would: split on spaces, with double quotes
    /// holding a space or a pattern together and removed.
    /// </summary>
    public static string[] Arguments(string command)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        bool any = false;

        foreach (char c in command)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (c == ' ' && !quoted)
            {
                if (any)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        if (any)
        {
            args.Add(current.ToString());
        }

        return [.. args];
    }

    /// <summary>Text as the manual and a run are compared: each line's end trimmed, and no blank lines at either end.</summary>
    public static string Normalize(string text) =>
        string.Join('\n', text.ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd())).Trim('\n');

    [GeneratedRegex(@"^(?<fence>`{3,})(?<info>[^`]*)$")]
    private static partial Regex Fence();

    [GeneratedRegex(@"^<!--\s*cli-example:\s*(?<tag>[^>]+?)\s*-->$")]
    private static partial Regex Tag();
}
