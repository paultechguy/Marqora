// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;

namespace PaulTechGuy.MQ.Cli.Inputs;

/// <summary>A file that could not be read as markdown: not UTF-8, so not something to rewrite.</summary>
internal sealed class NotUtf8Exception : Exception
{
    public NotUtf8Exception()
        : base("The file is not valid UTF-8.")
    {
    }

    public NotUtf8Exception(string message)
        : base(message)
    {
    }

    public NotUtf8Exception(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A markdown file as it was on disk: its text, and the three things about its bytes a verb must
/// not change behind the user's back - a byte order mark, its line endings, and whether it ends
/// with a newline (CLI-IN-04, CLI-OUT-06).
///
/// Read strictly as UTF-8. A file in another encoding fails rather than being decoded with
/// replacement characters, which --write would then have saved over the original.
/// </summary>
internal sealed class TextFile
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private TextFile(byte[] bytes, bool hasBom, string text)
    {
        Bytes = bytes;
        HasBom = hasBom;
        Text = text;

        int crlf = 0;
        int other = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    other++;
                }
            }
            else if (text[i] == '\n')
            {
                other++;
            }
        }

        LineBreaks = crlf + other;
        IsMixed = crlf > 0 && other > 0;

        // A file with no line break at all says nothing about what it would use; LF is what the
        // verbs themselves write. Ties go to CRLF, as the app's formatter decides them.
        Ending = crlf >= other && crlf > 0 ? "\r\n" : "\n";
        EndsWithNewline = text.EndsWith('\n') || text.EndsWith('\r');
    }

    /// <summary>The bytes exactly as read, for telling whether a result changed anything.</summary>
    public byte[] Bytes { get; }

    public bool HasBom { get; }

    /// <summary>The text, with its line endings as they were.</summary>
    public string Text { get; }

    /// <summary>The ending most of its lines use: "\r\n" or "\n".</summary>
    public string Ending { get; }

    /// <summary>True when lines end both ways; such a file is written back with <see cref="Ending"/> throughout.</summary>
    public bool IsMixed { get; }

    public bool EndsWithNewline { get; }

    public int LineBreaks { get; }

    /// <exception cref="NotUtf8Exception">The bytes are not valid UTF-8.</exception>
    public static TextFile Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        bool hasBom = bytes.AsSpan().StartsWith(Bom);

        try
        {
            string text = Strict.GetString(bytes, hasBom ? Bom.Length : 0, bytes.Length - (hasBom ? Bom.Length : 0));
            return new TextFile(bytes, hasBom, text);
        }
        catch (DecoderFallbackException)
        {
            throw new NotUtf8Exception("is not valid UTF-8, so it was not read. Save it as UTF-8 and run again.");
        }
    }

    /// <summary>
    /// A verb's result, put back into this file's shape.
    ///
    /// A result equal to the text is the text, untouched, whatever its endings: a mixed file the
    /// verb did not change is not "changed" by having its endings tidied. Otherwise, unless the
    /// verb decides line endings itself - format does, through its own options (A7) - every line
    /// takes this file's ending, and the final newline is kept as it was: added back if the verb
    /// dropped it, dropped if the file had none.
    /// </summary>
    public string Restore(string result, bool verbOwnsEndings)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (verbOwnsEndings || result == Text)
        {
            return result;
        }

        var builder = new StringBuilder(result.Length + 64);

        for (int i = 0; i < result.Length; i++)
        {
            char c = result[i];

            if (c == '\r')
            {
                if (i + 1 < result.Length && result[i + 1] == '\n')
                {
                    i++;
                }

                builder.Append(Ending);
            }
            else if (c == '\n')
            {
                builder.Append(Ending);
            }
            else
            {
                builder.Append(c);
            }
        }

        string shaped = builder.ToString();

        if (!EndsWithNewline && shaped.EndsWith(Ending, StringComparison.Ordinal))
        {
            shaped = shaped[..^Ending.Length];
        }
        else if (EndsWithNewline && LineBreaks > 0 && !shaped.EndsWith('\n'))
        {
            shaped += Ending;
        }

        return shaped;
    }

    /// <summary>Text as this file's bytes: its byte order mark if it had one, then UTF-8.</summary>
    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] body = Strict.GetBytes(text);

        return HasBom ? [.. Bom, .. body] : body;
    }
}
