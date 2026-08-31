// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Formatting;

/// <summary>
/// Tidies markdown source without changing what it renders to.
///
/// Implementations must be pure: same text and options in, same text out, no I/O and no
/// state carried between calls. That is what makes the formatter safe to run on a
/// background thread and straightforward to test.
/// </summary>
public interface IMarkdownFormatter
{
    /// <summary>Formats a whole document.</summary>
    FormattedMarkdown Format(string markdown, FormatOptions options);

    /// <summary>
    /// Formats a run of whole lines, leaving the rest of the document untouched.
    ///
    /// The surrounding text is still supplied, because a line cannot be understood on its
    /// own: whether it sits inside a fenced code block or a table decides what may be done
    /// to it, and that is only knowable from what came before.
    /// </summary>
    FormattedMarkdown FormatLines(string markdown, int firstLine, int lastLine, FormatOptions options);
}
