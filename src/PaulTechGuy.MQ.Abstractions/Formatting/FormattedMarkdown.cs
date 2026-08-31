// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Formatting;

/// <summary>The result of a formatting pass.</summary>
/// <param name="Text">The formatted document.</param>
/// <param name="ChangedLines">
/// How many lines differ from the input. Zero means the document was already tidy, which is
/// worth telling the user rather than silently doing nothing.
/// </param>
public readonly record struct FormattedMarkdown(string Text, int ChangedLines)
{
    /// <summary>True when the formatter found nothing to do.</summary>
    public bool IsUnchanged => ChangedLines == 0;
}
