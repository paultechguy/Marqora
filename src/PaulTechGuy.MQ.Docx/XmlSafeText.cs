// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// Strips the characters XML cannot carry.
///
/// A markdown file is whatever the user pasted into it, and some of what people paste is not
/// expressible in XML 1.0 at all: a form feed or a vertical tab out of a PDF, a stray NUL out
/// of a binary file opened by mistake, or half of an emoji left behind when some other tool
/// truncated a string in the middle of a surrogate pair.
///
/// The Open XML SDK will write all of it without complaint, and the result is a .docx that no
/// reader can parse - including Word, which reports it as corrupt with nothing to say about
/// why. Since the cost of checking is one scan of text that is about to be written to disk
/// anyway, every string goes through here on its way into the document.
/// </summary>
internal static class XmlSafeText
{
    public static string Clean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!NeedsCleaning(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // A supplementary character - an emoji, most often - is two chars that are only
            // legal together. Copy the pair as a unit so the test below never sees either
            // half on its own.
            if (char.IsHighSurrogate(c)
                && i + 1 < text.Length
                && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append(c).Append(text[i + 1]);
                i++;

                continue;
            }

            if (IsLegal(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool NeedsCleaning(string text)
    {
        foreach (char c in text)
        {
            if (!IsLegal(c) || char.IsSurrogate(c))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// XML 1.0 section 2.2: tab, newline and carriage return, then everything from the space
    /// up, minus the two permanently unassigned code points at the end of the plane. A lone
    /// surrogate fails this, which is the point - it is only legal as half of a pair, and the
    /// caller has already copied the legal pairs past it.
    /// </summary>
    private static bool IsLegal(char c) =>
        c is '\t' or '\n' or '\r'
        || (c >= ' ' && c <= '퟿')
        || (c >= '' && c <= '�');
}
