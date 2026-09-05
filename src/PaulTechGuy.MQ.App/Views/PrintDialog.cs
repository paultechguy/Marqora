// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Marqora's print dialog: which printer, how many, and on what.
///
/// Marqora's own rather than the Windows one, and the reason is worth stating because calling
/// the system dialog is otherwise the obvious thing to do. Windows 11 no longer shows the
/// dialog a PrintDlg call asks for. It substitutes its own modern print experience, drawn by
/// the system in the system's theme - so a light Marqora on a dark Windows opened a dark
/// dialog and nothing in this process could reach it. Worse, that dialog collects settings it
/// then hands back in a DEVMODE, of which the old code read five fields: a user who chose
/// grayscale there got the printer's default instead. See docs/DialogTheming.md.
///
/// So this asks the questions itself, and every question it asks reaches the paper. What it
/// offers comes from the chosen printer rather than from a table: <see cref="Win32Printers"/>
/// asks the driver what paper it holds and whether it can print in color or on both sides,
/// and a capability the driver does not claim is not offered at all.
///
/// Margins and backgrounds are not here. They come from the PDF page setup in preferences, so
/// paper and PDF start from the same idea of what a Marqora page looks like - which is what
/// the dialog this replaces did, having no field for either.
/// </summary>
internal sealed class PrintDialog : ContentDialog
{
    private const string AllPagesPlaceholder = "All pages";

    /// <summary>
    /// How wide the page-range explanation is allowed to get. Under the dialog's own 360 so
    /// the tooltip reads as a note about one field rather than a second panel.
    /// </summary>
    private const double HintWidth = 250;

    private static readonly string[] Orientations = ["Portrait", "Landscape"];
    private static readonly string[] ColorModes = ["Color", "Black and white"];

    private static readonly string[] DuplexModes =
        ["One-sided", "Two-sided, flip on long edge", "Two-sided, flip on short edge"];

    private readonly PdfPageSetup _defaults;
    private readonly IReadOnlyList<string> _printers;

    private readonly ComboBox _printer;
    private readonly NumberBox _copies;
    private readonly CheckBox _collate;
    private readonly TextBox _pages;
    private readonly TextBlock _pagesError;
    private readonly ComboBox _paper;
    private readonly ComboBox _orientation;
    private readonly ComboBox _color;
    private readonly ComboBox _duplex;
    private readonly StackPanel _colorField;
    private readonly StackPanel _duplexField;

    /// <summary>
    /// The chosen printer's capabilities. Replaced whenever the printer changes, because
    /// paper, color and duplex all belong to the printer rather than to the dialog.
    /// </summary>
    private PrinterCapabilities _capabilities;

    private PrintDialog(PdfPageSetup defaults)
    {
        _defaults = defaults;
        _printers = Win32Printers.Names();

        Title = "Print";
        PrimaryButtonText = "Print";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        _printer = DialogFields.Combo(_printers, IndexOfDefaultPrinter());
        _capabilities = Win32Printers.Capabilities(SelectedPrinter);

        _copies = new NumberBox
        {
            Value = 1,
            Minimum = 1,
            Maximum = _capabilities.MaximumCopies,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,

            // Snaps a nonsense entry back rather than reporting it: a copy count has one
            // rule, it is stated by the spinner, and an error line for it would be noise.
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            Width = 140,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        _collate = new CheckBox
        {
            Content = "Collate copies",
            IsChecked = true,
            IsEnabled = false,
        };

        _pages = new TextBox { PlaceholderText = AllPagesPlaceholder };

        // Deliberately unstyled, where PreferencesWindow paints its complaints with
        // SystemFillColorCriticalBrush. A brush fetched from Application.Current.Resources
        // resolves against the *application's* theme - the operating system's, not the one
        // the user chose here - and this dialog exists because of exactly that kind of
        // mismatch. The inherited foreground is right in both themes; the sentence and the
        // disabled Print button carry the warning. See MqStyles.
        _pagesError = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.9,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };

        _paper = DialogFields.Combo(PaperNames(), IndexOfDefaultPaper());
        _orientation = DialogFields.Combo(Orientations, (int)_defaults.Orientation);
        _color = DialogFields.Combo(ColorModes, 0);
        _duplex = DialogFields.Combo(DuplexModes, 0);

        _colorField = DialogFields.Labelled("Color", _color);
        _duplexField = DialogFields.Labelled("Sides", _duplex);

        _printer.SelectionChanged += OnPrinterChanged;
        _copies.ValueChanged += OnCopiesChanged;
        _pages.TextChanged += OnPagesChanged;

        Content = BuildContent();

        ApplyCapabilities();
        IsPrimaryButtonEnabled = _printers.Count > 0;
    }

    /// <summary>
    /// The job the user asked for. Only meaningful when the dialog returned Primary, and only
    /// then is it read.
    /// </summary>
    public PrintJob Job
    {
        get
        {
            (double width, double height) = SelectedPageSize();

            return new PrintJob
            {
                PrinterName = SelectedPrinter,
                Copies = SelectedCopies(),
                Collate = _collate.IsEnabled && _collate.IsChecked == true,
                Orientation = (PageOrientation)Math.Max(0, _orientation.SelectedIndex),
                WidthInches = width,
                HeightInches = height,
                MarginInches = _defaults.MarginInches,
                IncludeBackgrounds = _defaults.IncludeBackgrounds,
                ColorMode = SelectedColorMode(),
                Duplex = SelectedDuplex(),
                PageRanges = SelectedPageRanges(),
            };
        }
    }

    private string SelectedPrinter =>
        _printer.SelectedIndex >= 0 && _printer.SelectedIndex < _printers.Count
            ? _printers[_printer.SelectedIndex]
            : string.Empty;

    /// <summary>
    /// Shows the dialog. Returns null when the user cancels, and when there is no window to
    /// anchor it to - a dialog with no XamlRoot cannot be shown at all.
    /// </summary>
    /// <param name="anchor">
    /// The window content the dialog belongs to. It carries both the XamlRoot and the theme:
    /// a ContentDialog is hosted outside the element tree the theme is set on, so a dialog
    /// that skipped this would open in the framework's dark whatever the app was showing.
    /// </param>
    public static async Task<PrintJob?> ShowAsync(FrameworkElement? anchor, PdfPageSetup defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        if (anchor is null)
        {
            return null;
        }

        var dialog = new PrintDialog(defaults).AnchorTo(anchor);

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? dialog.Job : null;
    }

    private StackPanel BuildContent()
    {
        var panel = new StackPanel { Spacing = 14, Width = 360 };

        if (_printers.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No printers are installed. Add one in Windows Settings, "
                    + "then print again.",
                TextWrapping = TextWrapping.Wrap,
            });

            return panel;
        }

        panel.Children.Add(DialogFields.Labelled("Printer", _printer));
        panel.Children.Add(DialogFields.Labelled("Copies", _copies));
        panel.Children.Add(_collate);

        // The only field in the dialog whose answer has a syntax rather than a value, and so
        // the only one carrying a hint. The placeholder covers the empty case; the icon
        // covers the four it cannot.
        var pages = DialogFields.Labelled("Pages", _pages, PagesHint());
        pages.Children.Add(_pagesError);
        panel.Children.Add(pages);

        panel.Children.Add(DialogFields.Labelled("Paper size", _paper));
        panel.Children.Add(DialogFields.Labelled("Orientation", _orientation));
        panel.Children.Add(_colorField);
        panel.Children.Add(_duplexField);

        return panel;
    }

    /// <summary>
    /// The explanation behind the icon beside the Pages label.
    ///
    /// A grid rather than a sentence, because the question a reader has here is "what may I
    /// type", and four worked examples answer it at a glance where a sentence listing four
    /// formats has to be read twice. The error line under the box already handles the case
    /// where they typed something and it was wrong; this handles the case where they have
    /// not typed anything yet because they do not know what is allowed.
    ///
    /// Built from <see cref="PageRange.Examples"/> so a form added to the parser cannot go
    /// missing here - and built from it twice, since a Grid says nothing at all to a screen
    /// reader and <see cref="PagesHelpText"/> has to say the same thing in prose.
    /// </summary>
    private static ContentControl PagesHint()
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = HintWidth };

        panel.Children.Add(new TextBlock
        {
            Text = PageRange.EmptyMeansEverything,
            TextWrapping = TextWrapping.Wrap,
        });

        var table = new Grid { ColumnSpacing = 12, RowSpacing = 3 };

        table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (int row = 0; row < PageRange.Examples.Count; row++)
        {
            (string syntax, string meaning) = PageRange.Examples[row];

            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Semi-bold rather than a monospace family. The app names no monospace font
            // outside the editor, where it is the user's own choice and could be anything;
            // the Auto column already lines the four examples up, which is what the
            // monospace would have been for.
            var typed = new TextBlock { Text = syntax, FontWeight = FontWeights.SemiBold };

            Grid.SetRow(typed, row);
            Grid.SetColumn(typed, 0);
            table.Children.Add(typed);

            var means = new TextBlock { Text = meaning, TextWrapping = TextWrapping.Wrap };

            Grid.SetRow(means, row);
            Grid.SetColumn(means, 1);
            table.Children.Add(means);
        }

        panel.Children.Add(table);

        panel.Children.Add(new TextBlock
        {
            Text = PageRange.CountsFromOne,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });

        return DialogFields.Hint(panel, "Page range formats", PagesHelpText());
    }

    /// <summary>
    /// The same explanation as prose, for Narrator.
    ///
    /// "means" rather than a dash or a colon between the two halves: a screen reader given
    /// "5-: page 5 to the end" reads the punctuation out and the sentence stops making sense.
    /// </summary>
    private static string PagesHelpText() => string.Join(
        ' ',
        PageRange.EmptyMeansEverything,
        string.Join(' ', PageRange.Examples.Select(e => $"{e.Syntax} means {e.Meaning}.")),
        PageRange.CountsFromOne);

    /// <summary>
    /// Re-asks the printer what it can do and reshapes the dialog around the answer.
    ///
    /// Paper is rebuilt rather than filtered: two printers rarely hold the same list, and a
    /// paper the new one does not have must not survive the change.
    /// </summary>
    private void OnPrinterChanged(object sender, SelectionChangedEventArgs e)
    {
        _capabilities = Win32Printers.Capabilities(SelectedPrinter);

        _paper.Items.Clear();

        foreach (string name in PaperNames())
        {
            _paper.Items.Add(new ComboBoxItem { Content = name });
        }

        _paper.SelectedIndex = _capabilities.Papers.Count == 0 ? -1 : IndexOfDefaultPaper();

        ApplyCapabilities();
    }

    /// <summary>Hides what this printer cannot do, and re-caps what it can.</summary>
    private void ApplyCapabilities()
    {
        _colorField.Visibility = _capabilities.SupportsColor ? Visibility.Visible : Visibility.Collapsed;
        _duplexField.Visibility = _capabilities.SupportsDuplex ? Visibility.Visible : Visibility.Collapsed;

        _copies.Maximum = _capabilities.MaximumCopies;

        if (_copies.Value > _capabilities.MaximumCopies)
        {
            _copies.Value = _capabilities.MaximumCopies;
        }
    }

    /// <summary>
    /// Collating one copy means nothing, so the box is offered only once there are copies to
    /// gather. It keeps its state while disabled: a user who turned it off for three copies
    /// and went back to one has not changed their mind.
    /// </summary>
    private void OnCopiesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        _collate.IsEnabled = _copies.Value > 1;

    private void OnPagesChanged(object sender, TextChangedEventArgs e)
    {
        string typed = _pages.Text;

        if (string.IsNullOrWhiteSpace(typed))
        {
            _pagesError.Visibility = Visibility.Collapsed;
            IsPrimaryButtonEnabled = _printers.Count > 0;
            return;
        }

        bool valid = PageRange.TryParse(typed, out _, out string error);

        _pagesError.Text = error;
        _pagesError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;

        // Refused here rather than at print time. The print call rejects a range it cannot
        // read by failing the job, which arrives long after the box has gone.
        IsPrimaryButtonEnabled = valid && _printers.Count > 0;
    }

    /// <summary>
    /// How many copies, as a number the printer will take.
    ///
    /// A NumberBox reports an empty box as NaN rather than as its minimum, and NaN survives
    /// both Math.Max and the cast to int as something no one wants sent to a printer.
    /// </summary>
    private int SelectedCopies() =>
        double.IsNaN(_copies.Value)
            ? 1
            : (int)Math.Clamp(_copies.Value, 1, _capabilities.MaximumCopies);

    /// <summary>Null for the whole document, which is what an empty box means.</summary>
    private string? SelectedPageRanges() =>
        PageRange.TryParse(_pages.Text, out string normalised, out _) ? normalised : null;

    private PrintColorMode SelectedColorMode() =>
        !_capabilities.SupportsColor ? PrintColorMode.Default
            : _color.SelectedIndex == 1 ? PrintColorMode.Grayscale
            : PrintColorMode.Color;

    private PrintDuplex SelectedDuplex() =>
        !_capabilities.SupportsDuplex ? PrintDuplex.Default
            : _duplex.SelectedIndex switch
            {
                1 => PrintDuplex.LongEdge,
                2 => PrintDuplex.ShortEdge,
                _ => PrintDuplex.OneSided,
            };

    private IReadOnlyList<string> PaperNames() => [.. _capabilities.Papers.Select(paper => paper.Name)];

    /// <summary>The user's default printer, falling back to the first the spooler listed.</summary>
    private int IndexOfDefaultPrinter()
    {
        string? preferred = Win32Printers.Default();

        for (int i = 0; preferred is not null && i < _printers.Count; i++)
        {
            if (string.Equals(_printers[i], preferred, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// The paper the PDF page setup names, if this printer has it.
    ///
    /// Matched by the driver's own name, which is how "Letter" and "A4" are spelled on every
    /// driver worth the name. A printer that calls it something else falls back to its first
    /// paper rather than to a size it does not hold.
    /// </summary>
    private int IndexOfDefaultPaper()
    {
        string wanted = _defaults.Paper.ToString();

        for (int i = 0; i < _capabilities.Papers.Count; i++)
        {
            if (string.Equals(_capabilities.Papers[i].Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// The chosen paper in inches, with orientation applied.
    ///
    /// The driver's own numbers are held short-edge-first whatever it called them, so
    /// orientation is applied once, here, rather than reasoned about per paper. A driver that
    /// lists a rotated size of its own therefore still prints the way the orientation field
    /// says it will.
    /// </summary>
    private (double Width, double Height) SelectedPageSize()
    {
        PaperOption paper = _paper.SelectedIndex >= 0 && _paper.SelectedIndex < _capabilities.Papers.Count
            ? _capabilities.Papers[_paper.SelectedIndex]
            : new PaperOption("Letter", _defaults.WidthInches, _defaults.HeightInches);

        double shortEdge = Math.Min(paper.WidthInches, paper.HeightInches);
        double longEdge = Math.Max(paper.WidthInches, paper.HeightInches);

        return _orientation.SelectedIndex == (int)PageOrientation.Landscape
            ? (longEdge, shortEdge)
            : (shortEdge, longEdge);
    }
}
