// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Page setup for a Word export.
///
/// Built in code rather than XAML for the same reason as <see cref="PdfExportDialog"/>: it has
/// no bindings and exists only to return a choice. It opens on the setup held in preferences,
/// which the caller saves again afterwards - so exporting several documents in a row does not
/// mean re-answering the same question.
///
/// The three page questions are the same three the PDF dialog asks, and the answers are kept
/// apart from it: a document meant to be edited and one meant to be printed want different
/// margins, and someone who changes one should not find the other changed underneath them.
/// The lower group is <see cref="ExportLayout"/>, shared with the PDF dialog: one cover, one
/// contents, one header, whichever export draws them.
/// </summary>
internal sealed class WordExportDialog : ContentDialog
{
    private readonly ComboBox _paper;
    private readonly ComboBox _orientation;
    private readonly ComboBox _margin;
    private readonly LayoutFields _layout;

    public WordExportDialog(string documentName, DocxExportSetup current, ExportLayout layout)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(layout);

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

        _layout = new LayoutFields(layout);

        Content = BuildContent(documentName);
    }

    /// <summary>What the user chose. Only meaningful when the dialog returned Primary.</summary>
    public ExportChoice<DocxExportSetup> Choice => new(
        new DocxExportSetup
        {
            Paper = (PaperSize)Math.Max(0, _paper.SelectedIndex),
            Orientation = (PageOrientation)Math.Max(0, _orientation.SelectedIndex),
            Margin = (PageMargin)Math.Max(0, _margin.SelectedIndex),
        },
        _layout.Layout);

    /// <summary>
    /// Two columns (<see cref="DialogFields.TwoColumns"/>), the same shape as Print and Export
    /// to PDF: the page on the left, what is printed around the document on the right.
    /// </summary>
    private FrameworkElement BuildContent(string documentName)
    {
        StackPanel page = DialogFields.Column();

        page.Children.Add(new TextBlock
        {
            Text = documentName,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        page.Children.Add(DialogFields.Labeled("Paper size", _paper));
        page.Children.Add(DialogFields.Labeled("Orientation", _orientation));
        page.Children.Add(DialogFields.Labeled("Margins", _margin));

        StackPanel content = DialogFields.Column();

        content.Children.Add(DialogFields.Group(_layout.Boxes));

        content.Children.Add(new TextBlock
        {
            Text = "Word builds the contents when you open the file and it offers to update "
                + "fields. The title page is read from the front matter. These three "
                + "choices are shared with Export to PDF.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7,
        });

        return DialogFields.TwoColumns(this, page, content);
    }
}
