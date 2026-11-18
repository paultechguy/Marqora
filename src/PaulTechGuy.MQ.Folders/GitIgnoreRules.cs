// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Folders;

/// <summary>
/// Which paths a repository's <c>.gitignore</c> files name, so the tree can gray them.
///
/// Graying rather than hiding is what makes a simple reader good enough: a mistake changes a
/// shade, and the file is still there and still opens. So this reads the common subset and fails
/// toward "not ignored" for the rest.
///
/// Supported: comments, blank lines, <c>*</c>, <c>?</c>, <c>**</c>, a leading <c>/</c>, a
/// trailing <c>/</c>, and <c>!</c>, with the last matching rule winning. Rules in a deeper
/// <c>.gitignore</c> come after a shallower one's, as git reads them. A path inside an ignored
/// folder is ignored whatever a later rule says, which is git's rule too. Character classes
/// (<c>[abc]</c>) are not read: such a rule never matches. <c>.git/info/exclude</c> and the global
/// excludes file are not read.
///
/// Matching ignores case, as git does on Windows by default.
///
/// Paths are relative to the repository's top, separated by <c>/</c>.
/// </summary>
public sealed class GitIgnoreRules
{
    private readonly ImmutableArray<Rule> _rules;

    private GitIgnoreRules(ImmutableArray<Rule> rules) => _rules = rules;

    public static GitIgnoreRules Empty { get; } = new([]);

    public bool IsEmpty => _rules.IsEmpty;

    /// <summary>
    /// These rules followed by the ones in a <c>.gitignore</c> whose folder is
    /// <paramref name="directory"/> (relative to the repository's top; empty for the top).
    /// </summary>
    public GitIgnoreRules With(string directory, string text)
    {
        string baseDir = Trim(directory.Replace('\\', '/'));
        ImmutableArray<Rule>.Builder rules = _rules.ToBuilder();

        foreach (string raw in text.Split('\n'))
        {
            if (Rule.Parse(baseDir, raw.TrimEnd('\r')) is { } rule)
            {
                rules.Add(rule);
            }
        }

        return rules.Count == _rules.Length ? this : new GitIgnoreRules(rules.ToImmutable());
    }

    /// <summary>Whether a path, or any folder it is inside, is ignored.</summary>
    public bool IsIgnored(string relativePath, bool isDirectory)
    {
        if (_rules.IsEmpty)
        {
            return false;
        }

        string path = Trim(relativePath.Replace('\\', '/'));

        if (path.Length == 0)
        {
            return false;
        }

        // A folder that is ignored takes everything inside it along, and a later "!" rule cannot
        // bring a file back out of it.
        for (int slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
        {
            if (Decide(path[..slash], isDirectory: true))
            {
                return true;
            }
        }

        return Decide(path, isDirectory);
    }

    private bool Decide(string path, bool isDirectory)
    {
        for (int i = _rules.Length - 1; i >= 0; i--)
        {
            if (_rules[i].Matches(path, isDirectory))
            {
                return !_rules[i].Negated;
            }
        }

        return false;
    }

    private static string Trim(string path) => path.Trim('/');

    private sealed class Rule
    {
        private readonly string _baseDir;
        private readonly Regex _pattern;
        private readonly bool _directoryOnly;
        private readonly bool _anchored;

        private Rule(string baseDir, Regex pattern, bool negated, bool directoryOnly, bool anchored)
        {
            _baseDir = baseDir;
            _pattern = pattern;
            Negated = negated;
            _directoryOnly = directoryOnly;
            _anchored = anchored;
        }

        public bool Negated { get; }

        public static Rule? Parse(string baseDir, string line)
        {
            string text = TrimTrailingSpaces(line);

            if (text.Length == 0 || text[0] == '#')
            {
                return null;
            }

            bool negated = false;

            if (text[0] == '!')
            {
                negated = true;
                text = text[1..];
            }
            else if (text.StartsWith(@"\!", StringComparison.Ordinal) || text.StartsWith(@"\#", StringComparison.Ordinal))
            {
                text = text[1..];
            }

            bool directoryOnly = text.EndsWith('/');
            text = text.TrimEnd('/');

            if (text.Length == 0)
            {
                return null;
            }

            // A slash anywhere but the end ties the pattern to this .gitignore's folder. Without
            // one, it matches a name at any depth below it.
            bool anchored = text.Contains('/');
            text = text.TrimStart('/');

            if (text.Length == 0)
            {
                return null;
            }

            // Character classes are not read. Such a rule is dropped, which can only leave a file
            // ungrayed - the safe direction.
            if (text.Contains('['))
            {
                return null;
            }

            var regex = new Regex(
                "^" + ToRegex(text) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

            return new Rule(baseDir, regex, negated, directoryOnly, anchored);
        }

        public bool Matches(string path, bool isDirectory)
        {
            if (_directoryOnly && !isDirectory)
            {
                return false;
            }

            string sub;

            if (_baseDir.Length == 0)
            {
                sub = path;
            }
            else if (path.Length > _baseDir.Length
                && path.StartsWith(_baseDir, StringComparison.OrdinalIgnoreCase)
                && path[_baseDir.Length] == '/')
            {
                sub = path[(_baseDir.Length + 1)..];
            }
            else
            {
                return false;
            }

            if (!_anchored)
            {
                int slash = sub.LastIndexOf('/');
                sub = slash < 0 ? sub : sub[(slash + 1)..];
            }

            return _pattern.IsMatch(sub);
        }

        private static string ToRegex(string glob)
        {
            var regex = new StringBuilder();
            int i = 0;

            while (i < glob.Length)
            {
                char c = glob[i];

                if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    bool atStart = i == 0;
                    bool slashBefore = atStart || glob[i - 1] == '/';
                    int after = i + 2;
                    bool atEnd = after == glob.Length;
                    bool slashAfter = !atEnd && glob[after] == '/';

                    if (slashBefore && slashAfter)
                    {
                        // "**/" - zero or more folders.
                        regex.Append("(?:.*/)?");
                        i = after + 1;
                        continue;
                    }

                    if (slashBefore && atEnd && !atStart)
                    {
                        // "a/**" - everything inside a.
                        regex.Append(".*");
                        i = after;
                        continue;
                    }

                    if (atStart && atEnd)
                    {
                        regex.Append(".*");
                        i = after;
                        continue;
                    }

                    // Any other "**" is an ordinary star.
                    regex.Append("[^/]*");
                    i = after;
                    continue;
                }

                switch (c)
                {
                    case '*':
                        regex.Append("[^/]*");
                        break;

                    case '?':
                        regex.Append("[^/]");
                        break;

                    case '\\' when i + 1 < glob.Length:
                        i++;
                        regex.Append(Regex.Escape(glob[i].ToString()));
                        break;

                    default:
                        regex.Append(Regex.Escape(c.ToString()));
                        break;
                }

                i++;
            }

            return regex.ToString();
        }

        /// <summary>Trailing spaces are dropped unless the last one is escaped with a backslash.</summary>
        private static string TrimTrailingSpaces(string line)
        {
            int end = line.Length;

            while (end > 0 && line[end - 1] == ' ')
            {
                if (end >= 2 && line[end - 2] == '\\')
                {
                    return line[..(end - 2)] + " ";
                }

                end--;
            }

            return line[..end];
        }
    }
}
