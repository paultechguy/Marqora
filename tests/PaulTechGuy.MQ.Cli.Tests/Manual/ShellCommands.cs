// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Cli.Tests.Manual;

/// <summary>One marqora-cli command found in a shell block: where it is, and the arguments it would get.</summary>
/// <param name="Forwards">It hands on its caller's arguments (<c>@args</c>, <c>$*</c>), so what it lacks is the caller's to give.</param>
internal sealed record ShellCommand(int Line, string Source, IReadOnlyList<string> Arguments, bool Forwards);

/// <summary>
/// The marqora-cli commands in a block of PowerShell, cmd or bash, read just well enough to check
/// their options against the parser. A command ends where the shell takes over again: a pipe, a
/// redirection, <c>&amp;&amp;</c>, a closing bracket. What a shell would put in place of a variable
/// or a substitution stands as a file name, which is what every one of them is in the recipes, and
/// one fed by <c>xargs</c> is given a file, as xargs gives it.
///
/// It is not a shell. A recipe that needs more than this to be read belongs in a script of its own.
/// </summary>
internal static class ShellCommands
{
    private const string Placeholder = "FILE.md";

    private static readonly HashSet<string> Shells = new(StringComparer.OrdinalIgnoreCase) { "powershell", "pwsh", "bat", "cmd", "bash", "sh" };

    private static readonly HashSet<string> Forwarding = new(StringComparer.Ordinal) { "@args", "$*", "%*", "$@" };

    public static bool IsShell(FencedBlock block) => Shells.Contains(block.Info.Trim());

    public static IEnumerable<ShellCommand> In(FencedBlock block)
    {
        if (!IsShell(block))
        {
            yield break;
        }

        bool cmd = block.Info.Trim() is "bat" or "cmd";

        foreach ((int offset, string line) in LogicalLines(block.Lines))
        {
            string trimmed = line.TrimStart();

            if (trimmed.StartsWith('#') || (cmd && (trimmed.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("::", StringComparison.Ordinal))))
            {
                continue;
            }

            foreach (int start in Occurrences(line))
            {
                bool fed = line[..start].Contains("xargs", StringComparison.Ordinal);
                (List<string> args, bool forwards) = Tokens(line, start);

                if (fed)
                {
                    args.Add(Placeholder);
                }

                yield return new ShellCommand(block.Line + 1 + offset, line.Trim(), args, forwards);
            }
        }
    }

    /// <summary>The block's lines, with a line ending in a continuation (a backtick, a backslash or a caret) joined to the next.</summary>
    private static IEnumerable<(int Offset, string Line)> LogicalLines(IReadOnlyList<string> lines)
    {
        var joined = new StringBuilder();
        int first = 0;

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];

            if (joined.Length == 0)
            {
                first = i;
            }

            if (line.EndsWith(" `", StringComparison.Ordinal) || line.EndsWith(" \\", StringComparison.Ordinal) || line.EndsWith(" ^", StringComparison.Ordinal))
            {
                joined.Append(line[..^1]);
                continue;
            }

            joined.Append(line);
            yield return (first, joined.ToString());
            joined.Clear();
        }

        if (joined.Length > 0)
        {
            yield return (first, joined.ToString());
        }
    }

    /// <summary>Where each command starts: the name, outside quotes, as a word of its own, just past it.</summary>
    private static IEnumerable<int> Occurrences(string line)
    {
        char quote = '\0';

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (quote != '\0')
            {
                quote = c == quote ? '\0' : quote;
                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (i > 0 && (char.IsLetterOrDigit(line[i - 1]) || line[i - 1] is '\\' or '/' or '-' or '.' or '_'))
            {
                continue;
            }

            foreach (string name in (string[])["marqora-cli.exe", "marqora-cli"])
            {
                int end = i + name.Length;

                if (string.CompareOrdinal(line, i, name, 0, name.Length) == 0 && (end == line.Length || char.IsWhiteSpace(line[end])))
                {
                    yield return end;
                    i = end - 1;
                    break;
                }
            }
        }
    }

    private static (List<string> Args, bool Forwards) Tokens(string line, int start)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool any = false;
        bool forwards = false;

        void Finish()
        {
            if (!any)
            {
                return;
            }

            string token = current.ToString();

            if (Forwarding.Contains(token))
            {
                forwards = true;
            }
            else
            {
                args.Add(token);
            }

            current.Clear();
            any = false;
        }

        for (int i = start; i < line.Length; i++)
        {
            char c = line[i];

            if (c is '"' or '\'')
            {
                int close = line.IndexOf(c, i + 1);
                close = close < 0 ? line.Length : close;
                current.Append(line, i + 1, close - i - 1);
                any = true;
                i = close;
            }
            else if (c == '(' || (c == '$' && i + 1 < line.Length && line[i + 1] == '('))
            {
                // A substitution: whatever it prints, which in the recipes is file names.
                int depth = 0;

                for (; i < line.Length; i++)
                {
                    depth += line[i] == '(' ? 1 : line[i] == ')' ? -1 : 0;

                    if (depth == 0 && line[i] == ')')
                    {
                        break;
                    }
                }

                current.Append(Placeholder);
                any = true;
            }
            else if (c is '|' or ';' or ')' or '}' or '&')
            {
                break;
            }
            else if (c is '>' or '<')
            {
                // "2>", "*>" and "1>" name a stream, not a file to work on.
                string token = current.ToString();

                if (token == "*" || (token.Length > 0 && token.All(char.IsAsciiDigit)))
                {
                    current.Clear();
                    any = false;
                }

                break;
            }
            else if (char.IsWhiteSpace(c))
            {
                Finish();
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        Finish();

        return (args, forwards);
    }
}
