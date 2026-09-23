// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// How much text a table cell holds, for the two places that size a column from its content:
/// the Word export's column shares and the preview's decision about which columns may wrap.
/// One measure, so the two cannot disagree about which column is the short one.
/// </summary>
public static class TableCellText
{
    /// <summary>
    /// Characters a reader sees, with the markup taken away. A cell holding a bold word is as
    /// wide as one holding the same word plain; a link counts its label and not its address.
    /// </summary>
    public static int LengthOf(TableCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);

        int length = 0;

        foreach (ParagraphBlock paragraph in cell.Descendants<ParagraphBlock>())
        {
            length += InlinePlainText.Of(paragraph.Inline).Trim().Length;
        }

        return length;
    }
}
