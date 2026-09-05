// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Page setup for a PDF export.
///
/// Built in code rather than XAML because it has no bindings and exists only to return a
/// <see cref="PdfPageSetup"/>. It opens on the setup held in preferences, which the caller
/// saves again afterwards - so exporting several documents in a row does not mean
/// re-answering the same question, and neither does coming back tomorrow.
/// </summary>
internal sealed class PdfExportDialog : ContentDialog
{
    private readonly ComboBox _paper;
    private readonly ComboBox _orientation;
    private readonly ComboBox _margin;
    private readonly CheckBox _backgrounds;

    public PdfExportDialog(string documentName, PdfPageSetup current)
    {
        ArgumentNullException.ThrowIfNull(current);

        Title = "Export to PDF";
        PrimaryButtonText = "Export";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _paper = DialogFields.Combo(["Letter", "A4", "Legal"], (int)current.Paper);
        _orientation = DialogFields.Combo(["Portrait", "Landscape"], (int)current.Orientation);
        _margin = DialogFields.Combo(
            ["Normal (0.5 in)", "Narrow (0.25 in)", "Wide (1 in)", "None"],
            (int)current.Margin);

        _backgrounds = new CheckBox
        {
            Content = "Include background colors",
            IsChecked = current.IncludeBackgrounds,
        };

        Content = BuildContent(documentName);
    }

    /// <summary>The page setup the user chose. Only meaningful when the dialog returned Primary.</summary>
    public PdfPageSetup Setup => new()
    {
        Paper = (PaperSize)Math.Max(0, _paper.SelectedIndex),
        Orientation = (PageOrientation)Math.Max(0, _orientation.SelectedIndex),
        Margin = (PageMargin)Math.Max(0, _margin.SelectedIndex),
        IncludeBackgrounds = _backgrounds.IsChecked ?? true,
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
        panel.Children.Add(_backgrounds);

        panel.Children.Add(new TextBlock
        {
            Text = "Diagrams, code blocks and tables rely on their background colors. "
                + "Turning them off saves ink but flattens those blocks.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.7,
        });

        return panel;
    }
}
