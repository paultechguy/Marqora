// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// Turns the anchor names markdown uses into the bookmark names Word will accept, and hands
/// out the ids that pair a bookmark's start with its end.
///
/// The two vocabularies do not overlap. A markdown anchor is a GitHub slug -
/// <c>why-it-exists</c> - and Word will not have it: a bookmark name has to begin with a
/// letter or an underscore, may contain only letters, digits and underscores after that, and
/// may not exceed forty characters. Every slug with a hyphen in it, which is most of them,
/// is therefore illegal.
///
/// Names here begin with an underscore, which also makes them hidden - the same convention
/// Word's own table-of-contents bookmarks follow, and the reason a reader never sees these in
/// the Bookmarks dialog.
/// </summary>
internal sealed class BookmarkTable
{
    /// <summary>Word's own limit. Longer names are accepted by some writers and truncated by Word.</summary>
    private const int MaximumNameLength = 40;

    /// <summary>Leaves room for the underscore in front and the hash on the end.</summary>
    private const int MaximumStemLength = 32;

    private readonly Dictionary<string, string> _bySlug = new(StringComparer.Ordinal);

    /// <summary>
    /// Bookmark ids are unique across the document and a duplicate is one of the reliable
    /// ways to make Word call a file corrupt, so they come from here and nowhere else.
    /// </summary>
    private int _nextId;

    public int NextId() => _nextId++;

    /// <summary>
    /// The bookmark name for a markdown anchor, the same one every time it is asked for.
    /// </summary>
    public string NameFor(string slug)
    {
        ArgumentNullException.ThrowIfNull(slug);

        if (_bySlug.TryGetValue(slug, out string? existing))
        {
            return existing;
        }

        string name = Sanitize(slug);

        _bySlug[slug] = name;

        return name;
    }

    /// <summary>Whether a link's target was ever written as a bookmark.</summary>
    public bool Knows(string slug) => _bySlug.ContainsKey(slug);

    /// <summary>
    /// The legal name for a slug.
    ///
    /// Every illegal character becomes an underscore, which on its own would let two distinct
    /// headings collapse onto one name - "set-up" and "set up" both become "set_up", and a
    /// link to the second would jump to the first. A short hash of the original slug is
    /// appended so that distinct slugs stay distinct.
    /// </summary>
    private static string Sanitize(string slug)
    {
        var builder = new StringBuilder(MaximumNameLength);

        builder.Append('_');

        foreach (char c in slug)
        {
            if (builder.Length >= MaximumStemLength)
            {
                break;
            }

            builder.Append(char.IsAsciiLetterOrDigit(c) ? c : '_');
        }

        builder.Append('_');
        builder.Append(Hash(slug).ToString("x6", CultureInfo.InvariantCulture));

        return builder.ToString();
    }

    /// <summary>
    /// FNV-1a, truncated to six hex digits. Not a checksum - it only has to make two slugs
    /// that sanitize alike come out different, and it has to give the same answer on every
    /// run so a re-export produces the same file.
    /// </summary>
    private static uint Hash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        uint hash = offset;

        foreach (char c in value)
        {
            hash = (hash ^ c) * prime;
        }

        return hash & 0xFFFFFF;
    }
}
