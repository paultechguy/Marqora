// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The "which pages" answer, as typed.
///
/// The print API takes this as a string - "2-5", "1,4-6" - and objects at print time to
/// anything it cannot read. By then the dialog has closed and the complaint arrives as a
/// failed job, so the text is checked while the user can still see the box they typed it in.
///
/// Held here rather than in the dialog because it is arithmetic on a string and nothing else:
/// no page count is known when the dialog is open, so an open-ended range cannot be checked
/// against a document and is not rejected for running past its end.
/// </summary>
public static class PageRange
{
    /// <summary>
    /// What an empty box means. Said in the dialog rather than left to be discovered, and
    /// stated here because it is a fact about this parser: <see cref="TryParse"/> treats
    /// whitespace as empty, and the dialog reads that as the whole document.
    /// </summary>
    public const string EmptyMeansEverything = "Leave this empty to print the whole document.";

    /// <summary>
    /// The rule <see cref="TryParsePage"/> enforces by refusing zero rather than quietly
    /// moving it to one.
    /// </summary>
    public const string CountsFromOne = "Pages count from 1.";

    /// <summary>
    /// Every form <see cref="TryParse"/> accepts, and what each one means.
    ///
    /// Here rather than in the dialog that shows them, because this is the grammar's own
    /// description and the two drift apart the moment they live in different files. The
    /// open-ended "5-" is the case that makes the point: it is the form nobody guesses, it
    /// is documented in this file only as a comment on the branch that reads it, and until
    /// now the dialog's placeholder and every error message left it out.
    /// </summary>
    public static readonly IReadOnlyList<(string Syntax, string Meaning)> Examples =
    [
        ("3", "just page 3"),
        ("2-5", "pages 2 to 5"),
        ("1,4-6", "page 1, then 4 to 6"),
        ("5-", "page 5 to the end"),
    ];

    /// <summary>
    /// Reads a page range, or explains itself.
    ///
    /// Whitespace is allowed anywhere and dropped, so "1, 4-6" is the same answer as "1,4-6".
    /// The normalised form is what goes to the printer.
    /// </summary>
    public static bool TryParse(string? text, out string normalised, out string error)
    {
        normalised = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Type a page or a range, such as 2-5.";
            return false;
        }

        List<string> parts = [];

        foreach (string part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryParsePart(part.Trim(), out string piece, out error))
            {
                return false;
            }

            parts.Add(piece);
        }

        if (parts.Count == 0)
        {
            error = "Type a page or a range, such as 2-5.";
            return false;
        }

        normalised = string.Join(',', parts);
        return true;
    }

    /// <summary>One comma-separated piece: a page, or two pages with a dash between them.</summary>
    private static bool TryParsePart(string part, out string normalised, out string error)
    {
        normalised = string.Empty;
        error = string.Empty;

        string[] ends = part.Split('-');

        if (ends.Length > 2)
        {
            error = $"\"{part}\" has more than one dash.";
            return false;
        }

        if (!TryParsePage(ends[0], out int first))
        {
            error = $"\"{part}\" is not a page number.";
            return false;
        }

        if (ends.Length == 1)
        {
            normalised = first.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        // "5-" is how the print API is told "from five to the end", and is left as it was
        // typed: there is no page count here to resolve it against, and none is needed.
        if (ends[1].Length == 0)
        {
            normalised = $"{first}-";
            return true;
        }

        if (!TryParsePage(ends[1], out int last))
        {
            error = $"\"{part}\" is not a page range.";
            return false;
        }

        if (last < first)
        {
            error = $"\"{part}\" ends before it starts.";
            return false;
        }

        normalised = $"{first}-{last}";
        return true;
    }

    /// <summary>
    /// A page number. Pages count from one, so zero is rejected rather than quietly moved:
    /// a user who typed 0-5 meant something, and printing 1-5 instead is a guess.
    /// </summary>
    private static bool TryParsePage(string text, out int page) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out page)
            && page > 0;
}
