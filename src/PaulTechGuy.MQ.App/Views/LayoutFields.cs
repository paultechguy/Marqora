// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The three <see cref="ExportLayout"/> boxes, as both export dialogs show them.
///
/// One class rather than three checkboxes written into each dialog, because the record is
/// shared and the labels, order and defaults must be too: a box called "Title page" in one
/// dialog and "Cover page" in the other would read as two settings when it is one.
/// </summary>
internal sealed class LayoutFields
{
    private readonly CheckBox _headerAndFooter;
    private readonly CheckBox _tableOfContents;
    private readonly CheckBox _coverPage;

    public LayoutFields(ExportLayout current)
    {
        ArgumentNullException.ThrowIfNull(current);

        _headerAndFooter = new CheckBox { Content = "Header and page numbers", IsChecked = current.IncludeHeaderAndFooter };
        _tableOfContents = new CheckBox { Content = "Table of contents", IsChecked = current.IncludeTableOfContents };
        _coverPage = new CheckBox { Content = "Title page", IsChecked = current.IncludeCoverPage };
    }

    /// <summary>The boxes, in the order a dialog shows them.</summary>
    public IEnumerable<CheckBox> Boxes => [_headerAndFooter, _tableOfContents, _coverPage];

    /// <summary>What the boxes say now.</summary>
    public ExportLayout Layout => new()
    {
        IncludeHeaderAndFooter = _headerAndFooter.IsChecked ?? true,
        IncludeTableOfContents = _tableOfContents.IsChecked ?? false,
        IncludeCoverPage = _coverPage.IsChecked ?? false,
    };
}
