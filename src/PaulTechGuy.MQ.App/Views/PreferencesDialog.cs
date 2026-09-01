// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.App.ViewModels;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Every setting the app has, in one place.
///
/// Changes apply as they are made, and Cancel puts them all back.
///
/// Applying live is what makes the dialog usable: most of these settings are visual - two
/// fonts, two sizes, the theme, heading numbers - and choosing a font size you cannot see
/// until you accept the dialog is a guess, then a reopen, then another guess. It also keeps
/// the eight settings that also sit on the View menu behaving the same whichever route is
/// taken to them, which a staged dialog could not: the menu applies instantly, and the two
/// would have disagreed about the same switch.
///
/// The cost of applying live used to be that there was no way back - Escape kept your
/// changes, which is the opposite of what Escape looks like it does. So the settings are
/// copied when the dialog opens and Cancel restores that copy. See PreferencesViewModel.
///
/// Four settings are held back until OK instead, because they act on the world rather than
/// only describing it, and Cancel could not undo what they had already done. See
/// CommitDeferred.
///
/// Built in code rather than XAML, following FormatOptionsDialog. The content is a table of
/// controls with a repetitive shape, and the layout helpers below express that far more
/// briefly than forty hand-written rows of markup would.
/// </summary>
internal sealed class PreferencesDialog : ContentDialog
{
    /// <summary>
    /// The dialog's content is a fixed size, not a size that follows the page on show.
    ///
    /// Both dimensions are pinned for the same reason: a ContentDialog measures itself
    /// against its content, and the six pages do not want the same room. The wrapping notes
    /// at the foot of each page are the worst of it - a paragraph with nowhere to wrap asks
    /// for as much width as it can get - so the dialog grew and shrank as the user moved
    /// down the sidebar, which is a lot of movement for a window nobody asked to resize.
    /// </summary>
    private const double ContentWidth = 680;

    private const double ContentHeight = 460;
    private const double SidebarWidth = 150;
    private const double FieldWidth = 240;

    /// <summary>Width of a field's label column, which FontHint indents past to line up.</summary>
    private const double LabelColumnWidth = 160;

    // Sidebar order, so a field can say which page it is on and OK can go there.
    private const int AppearancePage = 0;
    private const int EditorPage = 1;
    private const int PreviewPage = 2;
    private const int FilesPage = 3;
    private const int ExportPage = 4;
    private const int AdvancedPage = 5;

    /// <summary>
    /// A numeric field, with what it takes to send the user back to it.
    /// </summary>
    /// <param name="Box">The control itself.</param>
    /// <param name="Name">What the complaint calls it, which is not always what the page labels it.</param>
    /// <param name="Error">The line under it that carries the complaint. Collapsed until needed.</param>
    /// <param name="Page">Which sidebar page holds it - the field may not be the one on screen.</param>
    private sealed record NumericField(NumberBox Box, string Name, TextBlock Error, int Page);

    /// <summary>Every numeric field in the dialog, filled in as the pages are built.</summary>
    private readonly List<NumericField> _numericFields = [];

    /// <summary>
    /// A curated list rather than the machine's installed fonts.
    ///
    /// Enumerating fonts means going to DirectWrite, and the result is a list of several
    /// hundred entries in which the six that are any use for code are buried. The box is
    /// editable, so a font that is not on this list can still be typed in full.
    /// </summary>
    private static readonly string[] MonospaceFonts =
    [
        "Cascadia Code", "Cascadia Mono", "Consolas", "Courier New",
        "Lucida Console", "Fira Code", "JetBrains Mono", "Source Code Pro",
    ];

    private static readonly string[] UiFonts =
    [
        "Segoe UI Variable Text", "Segoe UI", "Calibri", "Verdana",
        "Arial", "Georgia", "Times New Roman",
    ];

    /// <summary>Shown in the font boxes for "whatever the stylesheet already uses".</summary>
    private const string DefaultFontLabel = "(default)";

    private readonly PreferencesViewModel _vm;
    private readonly ILogger _logger;

    /// <summary>
    /// Set while the controls are being filled in from settings.
    ///
    /// Every control here reports a change the moment its value is set, including when this
    /// dialog sets it. Without this, opening the dialog would write every setting back over
    /// itself, and Restore Defaults - which refills the controls - would write the old values
    /// straight back in behind the reset.
    /// </summary>
    private bool _loading;

    private readonly RadioButtons _theme;
    private readonly ComboBox _sourceFont;
    private readonly NumberBox _sourceFontSize;
    private readonly ComboBox _previewFont;
    private readonly NumberBox _previewFontSize;
    private readonly CheckBox _limitWidth;
    private readonly NumberBox _previewWidth;

    /// <summary>The lines under the two font boxes saying what is actually being drawn.</summary>
    private readonly TextBlock _sourceHint = FontHint();
    private readonly TextBlock _previewHint = FontHint();

    private readonly CheckBox _wordWrap;
    private readonly CheckBox _lineNumbers;
    private readonly CheckBox _showWhitespace;
    private readonly CheckBox _wrapGlyph;
    private readonly NumberBox _tabSize;
    private readonly CheckBox _insertSpaces;
    private readonly CheckBox _minimap;
    private readonly CheckBox _highlightLine;
    private readonly CheckBox _continueLists;
    private readonly CheckBox _autoCloseBrackets;
    private readonly NumberBox _wrapColumn;

    private readonly CheckBox _scrollSync;
    private readonly CheckBox _diagnostics;
    private readonly ComboBox _headingNumbers;

    private readonly ComboBox _startup;
    private readonly NumberBox _recentLimit;
    private readonly CheckBox _reloadOnChange;
    private readonly ComboBox _autoSave;
    private readonly NumberBox _autoSaveDelay;
    private readonly ComboBox _lineEnding;
    private readonly CheckBox _writeBom;

    private readonly ComboBox _paper;
    private readonly ComboBox _orientation;
    private readonly ComboBox _margin;
    private readonly CheckBox _backgrounds;

    private readonly NumberBox _logRetention;

    private readonly ContentControl _pageHost;
    private readonly ListView _categories;

    /// <summary>
    /// The dialog's content, kept only so the discard confirmation has something to anchor
    /// to. A ContentDialog's own buttons are inside its template and cannot be reached.
    /// </summary>
    private readonly Grid _shell;

    /// <summary>
    /// The six pages, built once.
    ///
    /// Not rebuilt on each selection, because the controls above are single instances shared
    /// with whichever page holds them - and adding an element that already has a parent
    /// throws. Building once also means the values survive paging back and forth without
    /// being re-read.
    /// </summary>
    private readonly UIElement[] _pages;

    public PreferencesDialog(PreferencesViewModel viewModel, ILogger logger)
    {
        _vm = viewModel;
        _logger = logger;

        Title = "Preferences";
        PrimaryButtonText = "OK";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;

        PrimaryButtonClick += OnAccept;
        CloseButtonClick += OnCancel;

        // A ContentDialog is 548px wide at most, which the template reads from this resource
        // rather than from MaxWidth - so setting MaxWidth on the dialog would do nothing and
        // the sidebar and its page would be squeezed into half the room they need. Overridden
        // locally rather than app-wide, because every other dialog is happy at the stock size.
        Resources["ContentDialogMaxWidth"] = 820d;

        // --------------------------------------------------------------- appearance
        _theme = new RadioButtons
        {
            Items = { "Use system setting", "Light", "Dark" },
        };

        _theme.SelectionChanged += (_, _) => Apply(() =>
        {
            if (_theme.SelectedIndex >= 0)
            {
                _vm.SetTheme((AppTheme)_theme.SelectedIndex);
            }
        });

        _sourceFont = BuildFontBox(MonospaceFonts);
        _sourceFont.TextSubmitted += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with { SourceFontFamily = ReadFont(_sourceFont) }));
        _sourceFont.SelectionChanged += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with { SourceFontFamily = ReadFont(_sourceFont) }));

        _sourceFontSize = BuildNumber(TypographyDefaults.MinimumFontSize, TypographyDefaults.MaximumFontSize);
        _sourceFontSize.ValueChanged += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with { SourceFontSize = ReadInt(_sourceFontSize, s.SourceFontSize) }));

        _previewFont = BuildFontBox(UiFonts);
        _previewFont.TextSubmitted += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with { PreviewFontFamily = ReadFont(_previewFont) }));
        _previewFont.SelectionChanged += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with { PreviewFontFamily = ReadFont(_previewFont) }));

        _previewFontSize = BuildNumber(TypographyDefaults.MinimumFontSize, TypographyDefaults.MaximumFontSize);
        _previewFontSize.SmallChange = 0.5;
        _previewFontSize.ValueChanged += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with
            {
                PreviewFontSize = double.IsNaN(_previewFontSize.Value) ? s.PreviewFontSize : _previewFontSize.Value,
            }));

        _limitWidth = BuildCheck("Limit the preview's width");
        _previewWidth = BuildNumber(
            TypographyDefaults.MinimumPreviewWidth,
            TypographyDefaults.MaximumPreviewWidth);
        _previewWidth.SmallChange = 20;

        _limitWidth.Checked += (_, _) => ApplyAsync(ApplyPreviewWidthAsync);
        _limitWidth.Unchecked += (_, _) => ApplyAsync(ApplyPreviewWidthAsync);
        _previewWidth.ValueChanged += (_, _) => ApplyAsync(ApplyPreviewWidthAsync);

        // ------------------------------------------------------------------- editor
        _wordWrap = BuildCheck("Word wrap");
        Bind(_wordWrap, v => _vm.SetWordWrapAsync(v));

        _lineNumbers = BuildCheck("Line numbers");
        Bind(_lineNumbers, v => _vm.SetLineNumbersAsync(v));

        _showWhitespace = BuildCheck("Show whitespace");
        Bind(_showWhitespace, v => _vm.SetShowWhitespaceAsync(v));

        _wrapGlyph = BuildCheck("Mark wrapped lines");
        Bind(_wrapGlyph, v => _vm.SetWrapGlyphAsync(v));

        _tabSize = BuildNumber(AppSettings.MinimumTabSize, AppSettings.MaximumTabSize);
        _tabSize.ValueChanged += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with { TabSize = ReadInt(_tabSize, s.TabSize) }));

        _insertSpaces = BuildCheck("Insert spaces instead of tabs");
        Bind(_insertSpaces, v => _vm.UpdateAsync(s => s with { InsertSpaces = v }));

        _minimap = BuildCheck("Show minimap");
        Bind(_minimap, v => _vm.UpdateAsync(s => s with { ShowMinimap = v }));

        _highlightLine = BuildCheck("Highlight the current line");
        Bind(_highlightLine, v => _vm.UpdateAsync(s => s with { HighlightCurrentLine = v }));

        _continueLists = BuildCheck("Continue lists when Enter is pressed");
        Bind(_continueLists, v => _vm.UpdateAsync(s => s with { ContinueLists = v }));

        _autoCloseBrackets = BuildCheck("Close brackets and quotes automatically");
        Bind(_autoCloseBrackets, v => _vm.UpdateAsync(s => s with { AutoCloseBrackets = v }));

        // The formatter's wrap width lives inside FormatRules rather than beside TabSize, so
        // it is written through the nested record. Formatting is the null-safe reader, which
        // matters for a settings file written before there were any format rules at all.
        _wrapColumn = BuildNumber(FormatOptions.MinimumWrapColumn, FormatOptions.MaximumWrapColumn);
        _wrapColumn.SmallChange = 5;
        _wrapColumn.ValueChanged += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with
            {
                FormatRules = s.Formatting with { WrapColumn = ReadInt(_wrapColumn, s.Formatting.WrapColumn) },
            }));

        // ------------------------------------------------------------------ preview
        _scrollSync = BuildCheck("Synchronize scrolling between the panes");
        Bind(_scrollSync, v => _vm.SetScrollSyncAsync(v));

        _diagnostics = BuildCheck("Underline problems in the source");
        Bind(_diagnostics, v => _vm.SetDiagnosticsAsync(v));

        _headingNumbers = BuildCombo(
            ["Off", "From heading 1", "From heading 2", "From heading 3"]);
        _headingNumbers.SelectionChanged += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with
            {
                HeadingNumbering = (HeadingNumbering)Math.Max(0, _headingNumbers.SelectedIndex),
            }));

        // -------------------------------------------------------------------- files
        _startup = BuildCombo(
            ["Reopen the last session", "Start with an empty tab", "Open the welcome document"]);
        _startup.SelectionChanged += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with
            {
                Startup = (StartupBehavior)Math.Max(0, _startup.SelectedIndex),
            }));

        // Deferred to OK - see CommitDeferred. No handler, because there is nothing to apply
        // until then; the value is read off the control when the dialog is accepted.
        _recentLimit = BuildNumber(AppSettings.MinimumRecentFilesLimit, AppSettings.MaximumRecentFilesLimit);

        _reloadOnChange = BuildCheck("Reload files changed on disk");
        _reloadOnChange.Checked += (_, _) => Apply(() => _vm.SetReloadOnExternalChange(true));
        _reloadOnChange.Unchecked += (_, _) => Apply(() => _vm.SetReloadOnExternalChange(false));

        // Deferred to OK as well, and for the sharper version of the same reason: autosave
        // that switched on the moment it was picked could write a file before the user had
        // decided they wanted it, and Cancel cannot unwrite that.
        _autoSave = BuildCombo(["Off", "When the window loses focus", "After a pause in typing"]);

        // The only live effect is greying the delay box, which is this dialog's own business
        // rather than a change to the settings.
        _autoSave.SelectionChanged += (_, _) => Apply(UpdateEnabledState);

        _autoSaveDelay = BuildNumber(
            AppSettings.MinimumAutoSaveDelaySeconds,
            AppSettings.MaximumAutoSaveDelaySeconds);
        _autoSaveDelay.SmallChange = 5;

        _lineEnding = BuildCombo(["Match the platform", "Windows (CRLF)", "Unix (LF)"]);
        _lineEnding.SelectionChanged += (_, _) => ApplyAsync(() =>
            _vm.UpdateAsync(s => s with
            {
                NewFileLineEnding = (LineEndingStyle)Math.Max(0, _lineEnding.SelectedIndex),
            }));

        _writeBom = BuildCheck("Write a UTF-8 byte order mark in new files");
        Bind(_writeBom, v => _vm.UpdateAsync(s => s with { WriteUtf8Bom = v }));

        // ---------------------------------------------------------- export and print
        _paper = BuildCombo(["Letter", "A4", "Legal"]);
        _paper.SelectionChanged += (_, _) => ApplyAsync(() => UpdatePdfAsync(setup => setup with
        {
            Paper = (PaperSize)Math.Max(0, _paper.SelectedIndex),
        }));

        _orientation = BuildCombo(["Portrait", "Landscape"]);
        _orientation.SelectionChanged += (_, _) => ApplyAsync(() => UpdatePdfAsync(setup => setup with
        {
            Orientation = (PageOrientation)Math.Max(0, _orientation.SelectedIndex),
        }));

        _margin = BuildCombo(["Normal (0.5 in)", "Narrow (0.25 in)", "Wide (1 in)", "None"]);
        _margin.SelectionChanged += (_, _) => ApplyAsync(() => UpdatePdfAsync(setup => setup with
        {
            Margin = (PageMargin)Math.Max(0, _margin.SelectedIndex),
        }));

        _backgrounds = BuildCheck("Include background colours");
        Bind(_backgrounds, v => UpdatePdfAsync(setup => setup with { IncludeBackgrounds = v }));

        // ----------------------------------------------------------------- advanced
        // Deferred with the other two, though this one only ever takes effect at the next
        // launch anyway: logging is configured before the settings service exists.
        _logRetention = BuildNumber(0, AppSettings.MaximumLogRetentionDays);

        _pageHost = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
        };

        _categories = new ListView
        {
            Width = SidebarWidth,
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = new[] { "Appearance", "Editor", "Preview", "Files", "Export & Print", "Advanced" },
        };

        _pages =
        [
            BuildAppearancePage(),
            BuildEditorPage(),
            BuildPreviewPage(),
            BuildFilesPage(),
            BuildExportPage(),
            BuildAdvancedPage(),
        ];

        _categories.SelectionChanged += (_, _) => ShowPage(_categories.SelectedIndex);

        _shell = BuildShell();
        Content = _shell;

        // The shell re-measures after every preference change and says what it landed on.
        // Unsubscribed on close: the view model outlives this dialog, so a handler left
        // attached would keep it and every control alive until the app shut down.
        _vm.FontsResolved += OnFontsResolved;
        Closed += (_, _) => _vm.FontsResolved -= OnFontsResolved;

        /*
            Follow the theme while the dialog is open.

            DialogExtensions.AnchorTo gives a dialog the window's theme once, on the way in,
            because a ContentDialog sits in the popup root and never inherits a later change.
            That is enough for every other dialog in the app and not enough for this one: this
            is where the theme is changed from, so picking Dark on the Appearance page left the
            dialog itself sitting in Light until it was closed and reopened.

            Unsubscribed on close for the same reason as FontsResolved - the theme service is a
            singleton and outlives this dialog by a long way.
        */
        _vm.EffectiveThemeChanged += OnEffectiveThemeChanged;
        Closed += (_, _) => _vm.EffectiveThemeChanged -= OnEffectiveThemeChanged;

        Populate();

        _categories.SelectedIndex = 0;
    }

    // ------------------------------------------------------------------------- pages

    // ------------------------------------------------------------- accept and cancel

    /// <summary>
    /// OK. Commits the settings that were held back, and lets the dialog close.
    ///
    /// Everything else is already in force - it was applied as it was changed - so there is
    /// nothing here for the rest of them.
    /// </summary>
    private void OnAccept(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (FirstInvalidField() is { } invalid)
        {
            args.Cancel = true;
            Reveal(invalid);

            return;
        }

        Apply(() => _vm.CommitDeferred(ApplyDeferredTo));
    }

    // ----------------------------------------------------------------- validation

    /// <summary>
    /// The first field with nothing usable in it, clearing every complaint on the way through
    /// so a field that has since been fixed stops being marked.
    ///
    /// Validity is Value being a number. With validation switched off, a NumberBox sets its
    /// Value to NaN when what was typed cannot be read as one, and that is the whole test.
    ///
    /// Text is deliberately not consulted, though it looks like the obvious thing to read.
    /// NumberBox.Text is a formatted representation of the committed value, not the buffer
    /// being typed into, and it is wrong in both directions: it still holds the last good
    /// number while "aaa" is on screen, and it is empty on a field whose value was set in
    /// code and never typed into - which is every field, every time this dialog opens. Both
    /// of those shipped as bugs.
    /// </summary>
    private NumericField? FirstInvalidField()
    {
        NumericField? first = null;

        foreach (NumericField field in _numericFields)
        {
            field.Error.Visibility = Visibility.Collapsed;

            // A disabled box is not the user's to fix - the preview width when the width is
            // not limited, the autosave delay when autosave is off. Blocking OK on one would
            // demand a correction that cannot be made.
            if (first is null && field.Box.IsEnabled && !ShowsANumber(field.Box))
            {
                first = field;
            }
        }

        if (first is null)
        {
            /*
                Everything reads as a number, so settle the boxes before anything is stored.

                A NumberBox only re-reads what was typed when it loses focus. Clicking OK
                usually does that on the way past, but reaching OK by keyboard need not, and
                Value is what gets written to the settings file. Moving focus to the sidebar
                makes every box commit first.

                Deliberately after the check rather than before it: whether the control
                repairs bad input on losing focus is its business, and a repair that ran
                first would erase the very thing being looked for.
            */
            _categories.Focus(FocusState.Programmatic);
        }

        return first;
    }

    /// <summary>
    /// Whether what the box is showing can be read as a number.
    ///
    /// Taken from the TextBox inside the control, which is the only thing that is true at the
    /// moment it is asked. Neither of the NumberBox's own properties is: Value is a
    /// half-second behind whatever is being typed, because it is not recomputed until the box
    /// loses focus; and Text is a formatted view of the committed value that is simply empty
    /// on any field filled in from code, which is every field when this dialog opens. Reading
    /// each of those in turn is what produced the first two versions of this check, one of
    /// which passed "aaa" and the other of which rejected an untouched 18.
    ///
    /// Out-of-range numbers count as numbers. They are clamped rather than refused, which is
    /// the behaviour the control already had and is clear enough: 500 in a field that stops
    /// at 48 says what was wanted.
    /// </summary>
    private static bool ShowsANumber(NumberBox box)
    {
        if (InputBoxOf(box) is { } input)
        {
            return !string.IsNullOrWhiteSpace(input.Text)
                && double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _);
        }

        /*
            No TextBox to read, because only the page on screen is in the visual tree - the
            other five are built and held aside. Value is the right answer for those, and
            safely so: a box on another page has necessarily lost focus to get there, so it
            has committed whatever was typed into it. Nothing can be half-entered off-screen.
        */
        return !double.IsNaN(box.Value);
    }

    /// <summary>
    /// The editable TextBox inside a NumberBox, or null before the control has a template.
    ///
    /// Found by walking rather than by name: a NumberBox's template holds exactly one
    /// TextBox, so the first one down is the one being typed into, and that does not depend
    /// on the part keeping the name it has today.
    /// </summary>
    private static TextBox? InputBoxOf(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);

            if (child is TextBox found)
            {
                return found;
            }

            if (InputBoxOf(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>Takes the complaint off every field. Used when the values are all replaced.</summary>
    private void ClearFieldErrors()
    {
        foreach (NumericField field in _numericFields)
        {
            field.Error.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Puts the problem in front of the user: its page, its message, and the keyboard in the
    /// box itself.
    ///
    /// Selecting the page is the part that cannot be skipped. A field left blank on Files is
    /// invisible from Appearance, and an OK that refused to work without saying where the
    /// trouble was would be worse than the silence this replaces.
    /// </summary>
    private void Reveal(NumericField field)
    {
        field.Error.Text = $"{field.Name} needs a number between "
            + $"{field.Box.Minimum:0.##} and {field.Box.Maximum:0.##}.";

        field.Error.Visibility = Visibility.Visible;

        _categories.SelectedIndex = field.Page;

        // A turn later, because selecting the page swaps the content and the box is not laid
        // out - so cannot take focus, or be scrolled to - until that has happened.
        DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            field.Box.Focus(FocusState.Programmatic));
    }

    /// <summary>
    /// Cancel, and the Escape key, which raises this same event.
    ///
    /// Asks first when there is anything to lose. Changes here have already been applied and
    /// watched, so discarding them undoes something the user has seen happen - a heavier
    /// thing than abandoning a form that never took effect, and too heavy to do on a
    /// reflexive Escape without asking.
    ///
    /// The confirmation is a Flyout rather than a dialog because WinUI permits one
    /// ContentDialog at a time and we are inside one; the same wall the formatter rules ran
    /// into. Cancelling the close keeps this dialog up while the flyout is answered.
    /// </summary>
    private void OnCancel(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!HasChanges)
        {
            return;
        }

        args.Cancel = true;

        DiscardConfirmation().ShowAt(_shell, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.Bottom,
        });
    }

    private Flyout DiscardConfirmation()
    {
        var flyout = new Flyout();

        var discard = new Button { Content = "Discard" };
        var keep = new Button { Content = "Keep editing" };

        discard.Click += (_, _) =>
        {
            flyout.Hide();

            // Closed by hand because the close that would have done it was cancelled above.
            ApplyAsync(async () =>
            {
                await _vm.RevertAsync().ConfigureAwait(true);

                Hide();
            });
        };

        keep.Click += (_, _) => flyout.Hide();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { discard, keep },
        };

        flyout.Content = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 300,
            Children =
            {
                new TextBlock
                {
                    Text = "Discard the changes you have made? Your preferences go back to "
                        + "how they were when this dialog opened.",
                    TextWrapping = TextWrapping.Wrap,
                },
                buttons,
            },
        };

        return Themed(flyout);
    }

    /// <summary>
    /// Whether Cancel has anything to undo.
    ///
    /// Two halves, because the settings live in two places while the dialog is open: most are
    /// already applied and are compared against the snapshot, and the deferred few are still
    /// sitting in their controls with nothing written down yet.
    /// </summary>
    private bool HasChanges => _vm.HasLiveChanges || DeferredChanged;

    private bool DeferredChanged
    {
        get
        {
            AppSettings opening = _vm.Opening;

            return ApplyDeferredTo(opening) != opening;
        }
    }

    /// <summary>The deferred controls' values, written onto a settings record.</summary>
    private AppSettings ApplyDeferredTo(AppSettings settings) => settings with
    {
        RecentFilesLimit = ReadInt(_recentLimit, settings.RecentFilesLimit),
        AutoSave = (AutoSaveMode)Math.Max(0, _autoSave.SelectedIndex),
        AutoSaveDelaySeconds = ReadInt(_autoSaveDelay, settings.AutoSaveDelaySeconds),
        LogRetentionDays = ReadInt(_logRetention, settings.LogRetentionDays),
    };

    private Grid BuildShell()
    {
        var grid = new Grid { Width = ContentWidth, Height = ContentHeight, ColumnSpacing = 18 };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _pageHost,
            Padding = new Thickness(0, 0, 12, 0),
        };

        Grid.SetColumn(_categories, 0);
        Grid.SetColumn(scroller, 1);

        grid.Children.Add(_categories);
        grid.Children.Add(scroller);

        return grid;
    }

    private void ShowPage(int index) =>
        _pageHost.Content = _pages[Math.Clamp(index, 0, _pages.Length - 1)];

    private StackPanel BuildAppearancePage()
    {
        var panel = NewPage();

        panel.Children.Add(Heading("THEME"));
        panel.Children.Add(_theme);

        panel.Children.Add(Divider());
        panel.Children.Add(Heading("SOURCE PANE"));
        panel.Children.Add(Field("Font", _sourceFont));
        panel.Children.Add(_sourceHint);
        panel.Children.Add(NumberField("Size", _sourceFontSize, AppearancePage, "px", name: "The source pane size"));

        panel.Children.Add(Divider());
        panel.Children.Add(Heading("PREVIEW"));
        panel.Children.Add(Field("Font", _previewFont));
        panel.Children.Add(_previewHint);
        panel.Children.Add(NumberField("Size", _previewFontSize, AppearancePage, "px", name: "The preview size"));
        panel.Children.Add(_limitWidth);
        panel.Children.Add(NumberField("Maximum width", _previewWidth, AppearancePage, "px"));

        panel.Children.Add(Note(
            "The preview fills its pane unless a width is set here. Zoom is separate and is "
            + "remembered per pane, so a font size set here is the size at 100%."));

        return panel;
    }

    private StackPanel BuildEditorPage()
    {
        var panel = NewPage();

        panel.Children.Add(Heading("DISPLAY"));
        panel.Children.Add(_wordWrap);
        panel.Children.Add(_lineNumbers);
        panel.Children.Add(_showWhitespace);
        panel.Children.Add(_wrapGlyph);
        panel.Children.Add(_minimap);
        panel.Children.Add(_highlightLine);

        panel.Children.Add(Divider());
        panel.Children.Add(Heading("TYPING"));
        panel.Children.Add(NumberField("Tab size", _tabSize, EditorPage, "spaces"));
        panel.Children.Add(_insertSpaces);
        panel.Children.Add(_continueLists);
        panel.Children.Add(_autoCloseBrackets);

        panel.Children.Add(Note(
            "These four also appear on the View menu, where they can be flipped without "
            + "coming here. Both routes change the same setting."));

        // Its own section rather than another row under TYPING, because "Word wrap" three
        // rows above is soft wrapping that only changes what the pane looks like, and this
        // one rewrites the file. Sitting them together invites the wrong reading of both.
        panel.Children.Add(Divider());
        panel.Children.Add(Heading("FORMATTING"));
        panel.Children.Add(NumberField("Wrap paragraphs at", _wrapColumn, EditorPage, "columns"));

        panel.Children.Add(Note(
            "The width Format Document wraps to, and the width Format Markdown opens on. It "
            + "only takes effect when the formatter's \"Re-wrap paragraphs\" rule is on, "
            + "which it is not by default: re-wrapping rewrites every line of a paragraph."));

        return panel;
    }

    private StackPanel BuildPreviewPage()
    {
        var panel = NewPage();

        panel.Children.Add(Heading("BEHAVIOUR"));
        panel.Children.Add(_scrollSync);
        panel.Children.Add(_diagnostics);

        panel.Children.Add(Divider());
        panel.Children.Add(Heading("HEADING NUMBERS"));
        panel.Children.Add(Field("Number headings", _headingNumbers));

        panel.Children.Add(Note(
            "Numbers are added to the preview and never written into your markdown, so this "
            + "cannot change a file. They carry through to Print, to the PDF and HTML "
            + "exports, and to Copy as Rich Text.\n\n"
            + "The level chosen counts 1, 2, 3; levels below it become 1.1, 1.1.1 and so on. "
            + "A heading above that level is left unnumbered but still starts a new section, "
            + "so its sub-headings begin again at one. A document that skips a level - a "
            + "\"###\" directly under a \"#\" - is numbered by what is actually there rather "
            + "than being given a zero for the level it left out."));

        return panel;
    }

    private StackPanel BuildFilesPage()
    {
        var panel = NewPage();

        panel.Children.Add(Heading("STARTUP"));
        panel.Children.Add(Field("When Marqora opens", _startup));
        panel.Children.Add(NumberField("Recent files to keep", _recentLimit, FilesPage));

        panel.Children.Add(Divider());
        panel.Children.Add(Heading("SAVING"));
        panel.Children.Add(_reloadOnChange);
        panel.Children.Add(Field("Autosave", _autoSave));
        panel.Children.Add(NumberField("Save after", _autoSaveDelay, FilesPage, "seconds"));

        panel.Children.Add(Divider());
        panel.Children.Add(Heading("NEW FILES"));
        panel.Children.Add(Field("Line endings", _lineEnding));
        panel.Children.Add(_writeBom);

        panel.Children.Add(Note(
            "Autosave writes documents that already have a file. It leaves an untitled "
            + "document alone rather than asking where to put it, and leaves one whose file "
            + "changed on disk alone rather than overwriting the change.\n\n"
            + "The two new-file settings apply only the first time a document is written. A "
            + "file that was opened from disk keeps the encoding and endings it arrived with."));

        return panel;
    }

    private StackPanel BuildExportPage()
    {
        var panel = NewPage();

        panel.Children.Add(Heading("PAGE SETUP"));
        panel.Children.Add(Field("Paper", _paper));
        panel.Children.Add(Field("Orientation", _orientation));
        panel.Children.Add(Field("Margins", _margin));
        panel.Children.Add(_backgrounds);

        panel.Children.Add(Note(
            "Where Export to PDF starts from, and what Print uses for the margins and "
            + "backgrounds its own dialog has no field for. Changing the setup in the export "
            + "dialog updates these too."));

        return panel;
    }

    private StackPanel BuildAdvancedPage()
    {
        var panel = NewPage();

        panel.Children.Add(Heading("LOGS"));
        panel.Children.Add(NumberField("Keep logs for", _logRetention, AdvancedPage, "days"));
        panel.Children.Add(Note("Zero keeps every log. Takes effect the next time Marqora starts."));

        panel.Children.Add(Divider());
        panel.Children.Add(Heading("SETTINGS FILE"));

        var openFolder = new Button { Content = "Open settings folder" };
        openFolder.Click += (_, _) => ApplyAsync(_vm.OpenSettingsFolderAsync);

        panel.Children.Add(openFolder);

        panel.Children.Add(Divider());
        panel.Children.Add(Heading("ANOTHER MACHINE"));

        var export = new Button { Content = "Export preferences..." };
        var import = new Button { Content = "Import preferences..." };

        // The button is its own anchor for the report that follows, which is what puts the
        // answer where the user is already looking.
        export.Click += (_, _) => ApplyAsync(() => ExportPreferencesAsync(export));
        import.Click += (_, _) => ApplyAsync(() => ImportPreferencesAsync(import));

        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { export, import },
        });

        panel.Children.Add(Note(
            "Export writes every preference on these six pages to a file, along with the "
            + "version of Marqora that wrote it. Your open documents, window position, recent "
            + "files and search history are not included - they describe this machine rather "
            + "than your preferences.\n\n"
            + "Import brings across everything the running version understands, whichever "
            + "version wrote the file, and says afterwards what it could not use. Like every "
            + "other change here, an import is undone by Cancel."));

        panel.Children.Add(Divider());
        panel.Children.Add(Heading("RESET"));

        // A Flyout rather than a confirmation dialog: WinUI allows only one ContentDialog at
        // a time and throws on a second, and this is already inside one.
        //
        // Shown from a Click handler rather than hung off the button's Flyout property, so it
        // is built at the moment it is needed. Themed() reads the theme in force when the
        // flyout is made, and a flyout made here in the constructor would have been given
        // whatever the dialog's theme was before it was ever on screen.
        var reset = new Button { Content = "Restore all defaults..." };

        reset.Click += (_, _) => RestoreDefaultsConfirmation().ShowAt(reset);

        panel.Children.Add(reset);

        return panel;
    }

    private Flyout RestoreDefaultsConfirmation()
    {
        var flyout = new Flyout();

        var confirm = new Button
        {
            Content = "Restore defaults",
            Margin = new Thickness(0, 4, 0, 0),
        };

        confirm.Click += (_, _) =>
        {
            flyout.Hide();

            ApplyAsync(async () =>
            {
                await _vm.ResetAsync().ConfigureAwait(true);

                // The reset went through the settings record, so every control is still
                // showing the value it had a moment ago.
                Populate();
            });
        };

        flyout.Content = new StackPanel
        {
            Spacing = 10,
            MaxWidth = 280,
            Children =
            {
                new TextBlock
                {
                    Text = "Every preference goes back to how it shipped. Your open "
                        + "documents, window position and recent files are not touched.",
                    TextWrapping = TextWrapping.Wrap,
                },
                confirm,
            },
        };

        return Themed(flyout);
    }

    // ------------------------------------------------------ moving between machines

    /// <summary>
    /// Export.
    ///
    /// The deferred controls are folded onto the settings record on the way out. Those four
    /// are still only in their controls at this point, and a file that showed the autosave
    /// setting the user had a minute ago rather than the one on their screen would be wrong
    /// in the least detectable way possible.
    /// </summary>
    private async Task ExportPreferencesAsync(FrameworkElement anchor)
    {
        if (await _vm.ExportAsync(ApplyDeferredTo(_vm.Current)).ConfigureAwait(true) is not { } outcome)
        {
            return;
        }

        ShowReport(
            anchor,
            outcome.Succeeded ? ReportTone.Done : ReportTone.Problem,
            outcome.Succeeded ? "Preferences exported" : "Nothing was exported",
            outcome.Message);
    }

    /// <summary>
    /// Import.
    ///
    /// An ordinary change to the dialog, applied through the same path as Cancel and Restore
    /// Defaults - so the View menu, the editor and the theme move with it, and Cancel takes
    /// it all back.
    ///
    /// The deferred four are the fiddly part, and the two steps below are both needed. First
    /// this machine's own values for them are put back over the imported ones, so that
    /// applying an import cannot start autosaving or trim the recent list before OK has been
    /// pressed - the promise the whole deferral exists to keep. Then, once the controls have
    /// been refilled from the settings, the file's values for those four are written into the
    /// controls, so OK commits them with everything else. Skipping the first step would let an
    /// imported autosave setting act immediately; skipping the second would silently drop four
    /// preferences from every import.
    /// </summary>
    private async Task ImportPreferencesAsync(FrameworkElement anchor)
    {
        if (await _vm.ImportAsync().ConfigureAwait(true) is not { } result)
        {
            return;
        }

        if (result is { Succeeded: true, Settings: { } imported })
        {
            await _vm.ApplyImportedAsync(ApplyDeferredTo(imported)).ConfigureAwait(true);

            Populate();

            _loading = true;

            try
            {
                WriteDeferred(imported);
            }
            finally
            {
                _loading = false;
            }

            UpdateEnabledState();
        }

        (ReportTone tone, string headline) = result switch
        {
            { Succeeded: false } => (ReportTone.Problem, "Nothing was imported"),
            { IsPartial: true } => (ReportTone.Attention, "Preferences imported, in part"),
            _ => (ReportTone.Done, "Preferences imported"),
        };

        ShowReport(anchor, tone, headline, result.Describe());
    }

    /// <summary>How loudly a report needs to be said.</summary>
    private enum ReportTone
    {
        /// <summary>It worked, entirely.</summary>
        Done,

        /// <summary>It worked, but something in the file did not arrive as written.</summary>
        Attention,

        /// <summary>It did not work.</summary>
        Problem,
    }

    /// <summary>
    /// Gives a flyout the theme the dialog is actually wearing.
    ///
    /// The same trap DialogExtensions.AnchorTo documents for ContentDialog, and for the same
    /// reason: a flyout is hosted in the popup root, a sibling of the window's content rather
    /// than a child of it, so the RequestedTheme the theme service sets on Window.Content
    /// never reaches it. What it inherits instead is neither reliably the app's theme nor
    /// reliably the framework's, and the presenter's background and the text on it were
    /// resolving against different ones - which is white text on a white card in light mode,
    /// and black on black in dark. A report nobody can read is worse than no report.
    ///
    /// Both halves have to be set. RequestedTheme on the content fixes the text; the
    /// presenter draws the background behind it and takes its own, so a style carries the
    /// theme there too.
    ///
    /// Taken from the theme service at the moment of showing rather than from this dialog's
    /// ActualTheme. Two reasons, and both bite: the pages are built before the dialog is in
    /// the tree, so there is no meaningful ActualTheme to read at construction; and the
    /// Appearance page can change the theme while the dialog is up, which the service knows
    /// about first.
    /// </summary>
    private Flyout Themed(Flyout flyout)
    {
        ElementTheme theme = _vm.EffectiveTheme == AppTheme.Dark
            ? ElementTheme.Dark
            : ElementTheme.Light;

        if (flyout.Content is FrameworkElement content)
        {
            content.RequestedTheme = theme;
        }

        var presenter = new Style(typeof(FlyoutPresenter));

        presenter.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, theme));

        flyout.FlyoutPresenterStyle = presenter;

        return flyout;
    }

    /// <summary>
    /// What an export or an import came to, said beside the button that was pressed.
    ///
    /// A Flyout for the same reason every other answer in this dialog is one: WinUI permits a
    /// single ContentDialog at a time and this is already inside one.
    ///
    /// Built to be noticed rather than merely displayed. The first version was a paragraph of
    /// body text in a plain popup, which is easy to dismiss without reading and - once the
    /// theme went wrong - easy to miss altogether. So it now leads with a coloured rule and a
    /// glyph, states the outcome in a line of its own, and only then gives the detail. The
    /// colour is the one thing that says which of the three outcomes this was before a word
    /// has been read.
    ///
    /// Scrollable, because the import report grows a line for each kind of thing that did not
    /// come across, and a file from a much older build can produce several.
    /// </summary>
    private void ShowReport(FrameworkElement anchor, ReportTone tone, string headline, string detail)
    {
        Brush? tint = ToneBrush(tone);

        var glyph = new FontIcon
        {
            Glyph = ToneGlyph(tone),
            FontSize = 18,
            VerticalAlignment = VerticalAlignment.Top,

            // Nudged onto the cap height of the headline beside it, which sits lower than the
            // glyph's own box.
            Margin = new Thickness(0, 2, 0, 0),
        };

        var title = new TextBlock
        {
            Text = headline,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };

        var rule = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
        };

        if (tint is not null)
        {
            glyph.Foreground = tint;
            rule.Background = tint;
        }

        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { glyph, title },
                },
                new TextBlock
                {
                    Text = detail,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };

        var scroller = new ScrollViewer
        {
            MaxHeight = 300,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = body,
        };

        var card = new Grid { ColumnSpacing = 12, MaxWidth = ReportWidth };

        card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(rule, 0);
        Grid.SetColumn(scroller, 1);

        card.Children.Add(rule);
        card.Children.Add(scroller);

        Themed(new Flyout { Content = card }).ShowAt(anchor);
    }

    /// <summary>Wide enough for the longest report line without becoming a paragraph of prose.</summary>
    private const double ReportWidth = 380;

    /// <summary>
    /// The colour that says which outcome this was.
    ///
    /// Marqora's own amber rather than the system's caution colour, for the reason given
    /// beside MqWarnBrush in App.xaml: the system one moves with the OS and this has to stay
    /// legible against a known text colour. Both it and the critical red read acceptably in
    /// either theme, which matters because ThemeBrush resolves against the application's
    /// resources rather than this element's and so cannot be relied on to pick the theme's
    /// own shade.
    /// </summary>
    private Brush? ToneBrush(ReportTone tone) => tone switch
    {
        ReportTone.Problem => ThemeBrush("SystemFillColorCriticalBrush"),
        ReportTone.Attention => ThemeBrush("MqWarnBrush"),
        _ => AccentBrush(),
    };

    /// <summary>
    /// The user's Windows accent, in the shade this theme uses it.
    ///
    /// Built from the SystemAccentColor family rather than read from
    /// AccentFillColorDefaultBrush, and the difference is the point. The brush is a theme
    /// resource, and ThemeBrush can only ask the application for one - which resolves against
    /// the application's theme, not the dialog's, so a light app could be handed the dark
    /// theme's accent. These are plain colours, identical in both theme dictionaries, so
    /// picking the shade here from the theme actually in force is exact.
    ///
    /// The shades are the ones the framework's own accent fill uses: Dark1 in light, Light2
    /// in dark, where the raw accent is often too dark to read against the background. That
    /// is what makes the report the same colour as the OK button beneath it rather than
    /// merely the same hue - and it is the same pair the tab strip is tinted from.
    /// </summary>
    private Brush? AccentBrush()
    {
        string key = _vm.EffectiveTheme == AppTheme.Dark
            ? "SystemAccentColorLight2"
            : "SystemAccentColorDark1";

        return Application.Current.Resources.TryGetValue(key, out object? value)
            && value is Windows.UI.Color colour
                ? new SolidColorBrush(colour)
                : ThemeBrush("AccentFillColorDefaultBrush");
    }

    /// <summary>
    /// Segoe Fluent Icons: an error badge (U+E783), a warning triangle (U+E7BA) and a tick
    /// (U+E73E). Written as the characters themselves, as the rest of the app does - the code
    /// points are named here because a private-use glyph is unreadable in a diff.
    /// </summary>
    private static string ToneGlyph(ReportTone tone) => tone switch
    {
        ReportTone.Problem => "",
        ReportTone.Attention => "",
        _ => "",
    };

    // ------------------------------------------------------------------- populating

    /// <summary>
    /// Fills every control from the settings as they stand.
    ///
    /// Called once when the dialog opens and again after a reset. The guard is what makes it
    /// safe: without it, setting a control's value here would be indistinguishable from the
    /// user setting it, and the dialog would write everything it read straight back.
    /// </summary>
    private void Populate()
    {
        _loading = true;

        try
        {
            AppSettings s = _vm.Current;

            _theme.SelectedIndex = (int)s.Theme;

            WriteFont(_sourceFont, s.SourceFontFamily);
            _sourceFontSize.Value = s.SourceFontSize;
            WriteFont(_previewFont, s.PreviewFontFamily);
            _previewFontSize.Value = s.PreviewFontSize;

            bool limited = s.PreviewMaxWidth > 0;
            _limitWidth.IsChecked = limited;

            // A width to go back to when the box is ticked again, rather than a zero the
            // spinner would have to be walked up from.
            _previewWidth.Value = limited ? s.PreviewMaxWidth : DefaultPreviewWidth;

            _wordWrap.IsChecked = s.WordWrapEnabled;
            _lineNumbers.IsChecked = s.ShowLineNumbers;
            _showWhitespace.IsChecked = s.ShowWhitespace;
            _wrapGlyph.IsChecked = s.ShowWrapGlyph;
            _tabSize.Value = s.TabSize;
            _insertSpaces.IsChecked = s.InsertSpaces;
            _minimap.IsChecked = s.ShowMinimap;
            _highlightLine.IsChecked = s.HighlightCurrentLine;
            _continueLists.IsChecked = s.ContinueLists;
            _autoCloseBrackets.IsChecked = s.AutoCloseBrackets;
            _wrapColumn.Value = s.Formatting.WrapColumn;

            _scrollSync.IsChecked = s.ScrollSyncEnabled;
            _diagnostics.IsChecked = s.ShowDiagnostics;
            _headingNumbers.SelectedIndex = (int)s.HeadingNumbering;

            _startup.SelectedIndex = (int)s.Startup;
            _reloadOnChange.IsChecked = s.ReloadOnExternalChange;
            _lineEnding.SelectedIndex = (int)s.NewFileLineEnding;
            _writeBom.IsChecked = s.WriteUtf8Bom;

            WriteDeferred(s);

            PdfPageSetup pdf = s.PdfDefaults;
            _paper.SelectedIndex = (int)pdf.Paper;
            _orientation.SelectedIndex = (int)pdf.Orientation;
            _margin.SelectedIndex = (int)pdf.Margin;
            _backgrounds.IsChecked = pdf.IncludeBackgrounds;

            UpdateEnabledState();
            RefreshFontHints();

            // Every value has just been replaced, so any complaint standing against the old
            // ones is about text that is no longer there. Restoring defaults comes through
            // here, and used to leave a stale error behind it.
            ClearFieldErrors();
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>
    /// The four controls whose values are not written down until OK - see CommitDeferred.
    ///
    /// Separated out because import needs them on their own. It puts the file's preferences
    /// into force through the settings record, which by design cannot carry these four, so
    /// they are written straight into the controls afterwards and committed with the rest
    /// when the dialog is accepted.
    ///
    /// Caller's job to have set <see cref="_loading"/>: these controls report a change the
    /// moment they are assigned, exactly as they do when Populate fills them in.
    /// </summary>
    private void WriteDeferred(AppSettings s)
    {
        _recentLimit.Value = s.RecentFilesLimit;
        _autoSave.SelectedIndex = (int)s.AutoSave;
        _autoSaveDelay.Value = s.AutoSaveDelaySeconds;
        _logRetention.Value = s.LogRetentionDays;
    }

    /// <summary>A sensible measure for someone switching the width limit on for the first time.</summary>
    private const int DefaultPreviewWidth = 860;

    /// <summary>Greys out the fields that only mean something when another is set.</summary>
    private void UpdateEnabledState()
    {
        _previewWidth.IsEnabled = _limitWidth.IsChecked ?? false;
        _autoSaveDelay.IsEnabled = _autoSave.SelectedIndex == (int)AutoSaveMode.AfterDelay;
    }

    private Task ApplyPreviewWidthAsync()
    {
        UpdateEnabledState();

        int width = (_limitWidth.IsChecked ?? false)
            ? ReadInt(_previewWidth, DefaultPreviewWidth)
            : TypographyDefaults.UnlimitedPreviewWidth;

        return _vm.UpdateAsync(s => s with { PreviewMaxWidth = width });
    }

    private Task UpdatePdfAsync(Func<PdfPageSetup, PdfPageSetup> mutate) =>
        _vm.UpdateAsync(s => s with { PdfSetup = mutate(s.PdfDefaults) });

    // ---------------------------------------------------------------------- plumbing

    /// <summary>Runs a change unless the controls are only being filled in.</summary>
    private void Apply(Action change)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            change();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A preference could not be applied.");
        }
    }

    /// <summary>
    /// The asynchronous half of <see cref="Apply"/>.
    ///
    /// Fire-and-forget deliberately: these are control event handlers, there is nothing to
    /// await them, and a preference that fails to reach the editor is a cosmetic problem that
    /// belongs in the log rather than in front of someone mid-sentence.
    /// </summary>
    private async void ApplyAsync(Func<Task> change)
    {
        if (_loading)
        {
            return;
        }

        try
        {
            await change().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A preference could not be applied.");
        }
    }

    private void Bind(CheckBox box, Func<bool, Task> apply)
    {
        box.Checked += (_, _) => ApplyAsync(() => apply(true));
        box.Unchecked += (_, _) => ApplyAsync(() => apply(false));
    }

    private static string? ReadFont(ComboBox box)
    {
        string text = (box.SelectedItem as string ?? box.Text ?? string.Empty).Trim();

        return string.IsNullOrEmpty(text) || text == DefaultFontLabel ? null : text;
    }

    private static void WriteFont(ComboBox box, string? family)
    {
        box.SelectedItem = null;
        box.Text = family ?? DefaultFontLabel;
    }

    /// <summary>
    /// A box's value, clamped into its own range, or <paramref name="fallback"/> when there
    /// is no number in it.
    ///
    /// The clamp is belt and braces. NumberBox coerces against Minimum and Maximum itself,
    /// but validation is switched off on these boxes and this is the last point before a
    /// number reaches the settings file - a font size of 900 stored because the control let
    /// one through would be a bad afternoon.
    ///
    /// The fallback is only ever reached for a field the user is being sent back to fix, so
    /// it keeps the setting sane in the meantime rather than deciding anything.
    /// </summary>
    private static int ReadInt(NumberBox box, int fallback) =>
        double.IsNaN(box.Value) ? fallback : (int)Math.Clamp(box.Value, box.Minimum, box.Maximum);

    // ----------------------------------------------------------------- construction

    private static StackPanel NewPage() => new() { Spacing = 10 };

    private static ComboBox BuildFontBox(string[] families)
    {
        var box = new ComboBox
        {
            IsEditable = true,
            Width = FieldWidth,
        };

        box.Items.Add(DefaultFontLabel);

        foreach (string family in families)
        {
            box.Items.Add(family);
        }

        return box;
    }

    private static ComboBox BuildCombo(string[] labels)
    {
        var box = new ComboBox { Width = FieldWidth };

        foreach (string label in labels)
        {
            box.Items.Add(label);
        }

        return box;
    }

    private static NumberBox BuildNumber(double minimum, double maximum) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        SmallChange = 1,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        Width = FieldWidth,

        /*
            Validation is off so that bad input survives long enough to be pointed at.

            This used to be InvalidInputOverwritten, which quietly restored the last good
            value when the box lost focus. Clicking OK loses focus, so a field left blank or
            full of letters repaired itself on the way out and the dialog closed having
            changed nothing - with no hint that the edit had been thrown away. Someone who
            cleared a field, typed nothing, and pressed OK was told they had saved.

            Self-repair and flagging cannot both happen: whichever runs first wins, and the
            repair always would. So the control is told to leave the text alone, and
            FirstInvalidField below refuses OK instead. Out-of-range numbers are a different
            case and still clamp - see ReadInt - because 500 in a field that stops at 48 says
            plainly enough what was wanted.
        */
        ValidationMode = NumberBoxValidationMode.Disabled,
    };

    private static CheckBox BuildCheck(string label) => new() { Content = label, MinWidth = 0 };

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeights.SemiBold,
        CharacterSpacing = 80,
        Opacity = 0.7,
    };

    /// <summary>
    /// The line under a font picker that says what is actually being drawn.
    ///
    /// It answers two different questions with one line, depending on what is in the box:
    ///
    ///   - Nothing chosen: names the stack the stylesheet falls back to, because "(default)"
    ///     on its own asks the user to keep or replace something the dialog never names. The
    ///     stack comes from the shell, which reads it out of app.css, rather than a copy in
    ///     C# that could drift.
    ///   - A font chosen: names the font actually in use. This is the one that matters, and
    ///     the one nothing else could tell you - typing a font the machine does not have
    ///     changes nothing on screen and looks exactly like typing one it does.
    ///
    /// The quotes CSS needs around a multi-word family are stripped, being punctuation for a
    /// parser rather than for a reader.
    /// </summary>
    private static string FontHintText(string? chosen, string? stack, string? resolved)
    {
        if (string.IsNullOrWhiteSpace(chosen))
        {
            return string.IsNullOrWhiteSpace(stack)
                ? string.Empty
                : $"(default) is {stack.Replace("\"", string.Empty)}";
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            return string.Empty;
        }

        return string.Equals(chosen, resolved, StringComparison.OrdinalIgnoreCase)
            ? $"Using {resolved}"
            : $"Using {resolved} - {chosen} is not installed";
    }

    /// <summary>
    /// The line itself, empty until <see cref="RefreshFontHints"/> fills it in.
    ///
    /// Collapsed rather than absent when there is nothing to say, which happens only while
    /// the preview is still starting - rare, and not worth a blank line over.
    /// </summary>
    private static TextBlock FontHint() => new()
    {
        Visibility = Visibility.Collapsed,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        FontSize = 11,

        // Indented past the label column so it sits under the box it describes.
        Margin = new Thickness(LabelColumnWidth + 10, -4, 0, 0),
    };

    /// <summary>
    /// Rewrites both font lines from what the shell last reported.
    ///
    /// Called when the dialog is populated and again whenever the shell re-measures, which it
    /// does after every preference change - so choosing a font that turns out not to be
    /// installed says so straight away rather than looking like nothing happened.
    /// </summary>
    /// <summary>
    /// The shell has re-measured. Arrives on the UI thread already, because the WebView
    /// raises its messages there.
    /// </summary>
    private void OnFontsResolved(object? sender, EventArgs e) => RefreshFontHints();

    /// <summary>
    /// The theme has moved under us - from the Appearance page, or from Windows itself while
    /// the dialog is up. Repaint this dialog to match, since nothing else will.
    /// </summary>
    private void OnEffectiveThemeChanged(object? sender, AppTheme effective) =>
        RequestedTheme = effective == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;

    private void RefreshFontHints()
    {
        Write(_sourceHint, FontHintText(ReadFont(_sourceFont), _vm.DefaultSourceFont, _vm.ResolvedSourceFont));
        Write(_previewHint, FontHintText(ReadFont(_previewFont), _vm.DefaultPreviewFont, _vm.ResolvedPreviewFont));

        static void Write(TextBlock hint, string text)
        {
            hint.Text = text;
            hint.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        FontSize = 12,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private static Border Divider() => new()
    {
        Height = 1,
        Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
        Margin = new Thickness(0, 6, 0, 2),
    };

    /// <summary>A theme brush, or null when this build's resource set has no such key.</summary>
    private static Brush? ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out object? value) ? value as Brush : null;

    /// <summary>
    /// A numeric field, with a line kept beneath it for a complaint about its contents, and
    /// registered so that OK can find its way back here.
    ///
    /// The line is built collapsed and stays that way unless OK finds something wrong, so it
    /// costs no height in the ordinary case.
    /// </summary>
    /// <param name="name">
    /// What the complaint calls this field, when its label alone would not identify it. Both
    /// font sizes are labelled "Size" under their own heading, which reads correctly on the
    /// page and not at all in a sentence.
    /// </param>
    private StackPanel NumberField(
        string label,
        NumberBox box,
        int page,
        string? unit = null,
        string? name = null)
    {
        var error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Visibility = Visibility.Collapsed,

            // Indented past the label column, so the complaint sits under the box it is about.
            Margin = new Thickness(LabelColumnWidth + 10, 2, 0, 0),
        };

        if (ThemeBrush("SystemFillColorCriticalBrush") is { } critical)
        {
            error.Foreground = critical;
        }

        _numericFields.Add(new NumericField(box, name ?? label, error, page));

        // Take the complaint away the moment the box holds a number again, rather than
        // leaving it standing under a field that has already been put right.
        box.ValueChanged += (_, _) =>
        {
            if (!double.IsNaN(box.Value))
            {
                error.Visibility = Visibility.Collapsed;
            }
        };

        return new StackPanel { Children = { Field(label, box, unit), error } };
    }

    /// <summary>A labelled control, with an optional unit after it.</summary>
    private static Grid Field(string label, FrameworkElement control, string? unit = null)
    {
        var grid = new Grid { ColumnSpacing = 10 };

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        Grid.SetColumn(text, 0);
        Grid.SetColumn(control, 1);

        grid.Children.Add(text);
        grid.Children.Add(control);

        if (unit is not null)
        {
            var units = new TextBlock
            {
                Text = unit,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.7,
            };

            Grid.SetColumn(units, 2);
            grid.Children.Add(units);
        }

        return grid;
    }
}
