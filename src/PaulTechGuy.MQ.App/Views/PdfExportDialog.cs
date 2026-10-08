// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Page setup for a PDF export.
///
/// Built in code rather than XAML because it has no bindings and exists only to return a
/// choice. It opens on the setup held in preferences, which the caller
/// saves again afterwards - so exporting several documents in a row does not mean
/// re-answering the same question, and neither does coming back tomorrow.
/// </summary>
internal sealed class PdfExportDialog : ContentDialog
{
    private readonly ComboBox _paper;
    private readonly ComboBox _orientation;
    private readonly ComboBox _margin;
    private readonly CheckBox _backgrounds;
    private readonly LayoutFields? _layout;

    /// <summary>
    /// The setup the dialog opened on. The answer starts from it, so a choice the dialog does not
    /// show - the classic engine, which lives in preferences - comes through untouched rather
    /// than being reset by every export.
    /// </summary>
    private readonly PdfPageSetup _current;

    /// <param name="layout">
    /// The shared layout boxes to show, or null for a page that has none to offer - a diagram
    /// popped out on its own has no cover, contents or running header.
    /// </param>
    public PdfExportDialog(string documentName, PdfPageSetup current, ExportLayout? layout)
    {
        ArgumentNullException.ThrowIfNull(current);

        _current = current;
        _layout = layout is null ? null : new LayoutFields(layout);

        Title = "Export to PDF";
        PrimaryButtonText = "Export";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _paper = DialogFields.Combo(["Letter", "A4", "Legal"], (int)current.Paper);
        _orientation = DialogFields.Combo(["Portrait", "Landscape"], (int)current.Orientation);
        // Word's presets and Word's measurements, which this dialog now means too. Its Normal
        // was half an inch until a document exported both ways came out on two measures under
        // one word.
        _margin = DialogFields.Combo(PageMargins.Labels, (int)current.Margin);

        _backgrounds = new CheckBox
        {
            Content = "Shade code, tables and callouts",
            IsChecked = current.IncludeBackgrounds,
        };

        Content = BuildContent(documentName);
    }

    /// <summary>What the user chose. Only meaningful when the dialog returned Primary.</summary>
    public ExportChoice<PdfPageSetup> Choice => new(
        Setup,
        _layout?.Layout ?? throw new InvalidOperationException("This dialog was opened without the layout boxes."));

    /// <summary>The page setup the user chose.</summary>
    public PdfPageSetup Setup => _current with
    {
        Paper = (PaperSize)Math.Max(0, _paper.SelectedIndex),
        Orientation = (PageOrientation)Math.Max(0, _orientation.SelectedIndex),
        Margin = (PageMargin)Math.Max(0, _margin.SelectedIndex),
        IncludeBackgrounds = _backgrounds.IsChecked ?? true,
    };

    /// <summary>
    /// Two columns (<see cref="DialogFields.TwoColumns"/>), the same shape as Print: the page
    /// on the left - its size, way up and margins - and what is printed on it on the right.
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

        // The shading box with the layout boxes, one group: all four say what goes on the page.
        content.Children.Add(DialogFields.Group([_backgrounds, .. _layout?.Boxes ?? []]));

        content.Children.Add(new TextBlock
        {
            Text = "Unticked saves ink: code, table headers, callouts, quotes and diagrams "
                + "print without their gray or colored fill, and the text prints as before."
                + (_layout is null
                    ? string.Empty
                    : " The header, contents and title page are shared with Export to Word."),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7,
        });

        return DialogFields.TwoColumns(this, page, content);
    }
}
