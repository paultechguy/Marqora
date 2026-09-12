// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Page setup for a Word export.
///
/// Built in code rather than XAML for the same reason as <see cref="PdfExportDialog"/>: it has
/// no bindings and exists only to return a <see cref="DocxExportSetup"/>. It opens on the
/// setup held in preferences, which the caller saves again afterwards - so exporting several
/// documents in a row does not mean re-answering the same question.
///
/// The three page questions are the same three the PDF dialog asks, and the answers are kept
/// apart from it: a document meant to be edited and one meant to be printed want different
/// margins, and someone who changes one should not find the other changed underneath them.
/// What is new here is the lower group, which is Word-only - there is nowhere on a PDF to put
/// a table of contents field.
/// </summary>
internal sealed class WordExportDialog : ContentDialog
{
    private readonly ComboBox _paper;
    private readonly ComboBox _orientation;
    private readonly ComboBox _margin;
    private readonly CheckBox _headerAndFooter;
    private readonly CheckBox _tableOfContents;
    private readonly CheckBox _coverPage;

    public WordExportDialog(string documentName, DocxExportSetup current)
    {
        ArgumentNullException.ThrowIfNull(current);

        Title = "Export to Word";
        PrimaryButtonText = "Export";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _paper = DialogFields.Combo(["Letter", "A4", "Legal"], (int)current.Paper);
        _orientation = DialogFields.Combo(["Portrait", "Landscape"], (int)current.Orientation);
        // One list, shared with the PDF dialog and the preferences page. It was three lists,
        // and they had already drifted: two of them still read "Wide (1 in, 2 sides)", which
        // says one inch on two sides at least as readily as it says two inches at the sides.
        _margin = DialogFields.Combo(PageMargins.Labels, (int)current.Margin);

        _headerAndFooter = new CheckBox
        {
            Content = "Header and page numbers",
            IsChecked = current.IncludeHeaderAndFooter,
        };

        _tableOfContents = new CheckBox
        {
            Content = "Table of contents",
            IsChecked = current.IncludeTableOfContents,
        };

        _coverPage = new CheckBox
        {
            Content = "Title page",
            IsChecked = current.IncludeCoverPage,
        };

        Content = BuildContent(documentName);
    }

    /// <summary>What the user chose. Only meaningful when the dialog returned Primary.</summary>
    public DocxExportSetup Setup => new()
    {
        Paper = (PaperSize)Math.Max(0, _paper.SelectedIndex),
        Orientation = (PageOrientation)Math.Max(0, _orientation.SelectedIndex),
        Margin = (PageMargin)Math.Max(0, _margin.SelectedIndex),
        IncludeHeaderAndFooter = _headerAndFooter.IsChecked ?? true,
        IncludeTableOfContents = _tableOfContents.IsChecked ?? false,
        IncludeCoverPage = _coverPage.IsChecked ?? false,
    };

    private StackPanel BuildContent(string documentName)
    {
        var panel = new StackPanel { Spacing = 14, Width = 340 };

        panel.Children.Add(new TextBlock
        {
            Text = documentName,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        panel.Children.Add(DialogFields.Labelled("Paper size", _paper));
        panel.Children.Add(DialogFields.Labelled("Orientation", _orientation));
        panel.Children.Add(DialogFields.Labelled("Margins", _margin));

        panel.Children.Add(_headerAndFooter);
        panel.Children.Add(_tableOfContents);
        panel.Children.Add(_coverPage);

        panel.Children.Add(new TextBlock
        {
            Text = "Word builds the contents when you open the file and it offers to update "
                + "fields. A title page is a template: anything the front matter does not "
                + "fill in is left as a gray word to replace.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7,
        });

        return panel;
    }
}
