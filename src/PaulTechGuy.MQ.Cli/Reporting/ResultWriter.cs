// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli.Reporting;

/// <summary>
/// The only writer to stdout, and it writes only results: the document a text verb produced,
/// a report verb's listing, a diff, or a --json line (CLI-RPT-02). Under --json nothing but JSON
/// lines reaches stdout, so a script can read it line by line without guessing which is which.
/// </summary>
internal sealed class ResultWriter(TextWriter output, bool json)
{
    public bool IsJson => json;

    /// <summary>Text exactly as given: a document keeps its own line endings and final newline.</summary>
    public void Text(string text)
    {
        if (!json)
        {
            output.Write(text);
        }
    }

    /// <summary>A line of a listing, ended with a plain LF whatever the platform says.</summary>
    public void Line(string line)
    {
        if (!json)
        {
            output.Write(line);
            output.Write('\n');
        }
    }

    /// <summary>
    /// JSON that is one answer to the whole run rather than a line per input - themes list's
    /// array - written only under --json, on a line of its own.
    /// </summary>
    public void Raw(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (json)
        {
            output.Write(answer);
            output.Write('\n');
        }
    }

    public void Json(JsonLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (json)
        {
            output.Write(line.Serialize());
            output.Write('\n');
        }
    }
}
