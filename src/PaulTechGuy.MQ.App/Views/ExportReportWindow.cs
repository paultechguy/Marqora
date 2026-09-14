// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// What an export could not carry across, as a list to work through.
///
/// This was a ContentDialog saying "Some things could not be carried across" over a wall of up
/// to ten sentences. Three things were wrong with that and all three are the same thing: a
/// report of what is missing from a document is not a message, it is a task list. It needs to
/// scroll rather than stop at ten, it needs to say *where* each problem is, and it has to
/// survive being looked away from - the reader's next move is to go and fix line 679.
///
/// So it is a palette window like every other secondary window, and modeless like every other
/// window in the app. That has one consequence worth stating plainly, because it was the whole
/// design question: **the line numbers describe the document as it was exported**, and nothing
/// stops the reader editing it underneath them. Preventing that would mean either a read-only
/// editor - a state the app has never had - or real modality, which
/// <c>docs/Architecture.md</c> costs out and declines. The answer here is Find All's, which
/// that document names as the answer for exactly this window: watch the workspace, and the
/// first real edit says so and stops the rows navigating. A row that cannot be trusted does
/// not pretend it can.
/// </summary>
public sealed partial class ExportReportWindow : PaletteWindow
{
    /// <summary>
    /// Segoe Fluent Icons' warning triangle, as its codepoint rather than as the character.
    ///
    /// A private-use glyph pasted into source survives most things and not all of them - an
    /// editor saving as something other than UTF-8, a tool that normalizes text, a copy
    /// through a terminal - and what it becomes is a hollow box in the window rather than a
    /// build error. The escape cannot be mistranscribed.
    /// </summary>
    private const string WarningGlyph = "\uE7BA";

    /// <summary>For a report where nothing is broken and everything is only worth knowing.</summary>
    private const string InfoGlyph = "\uE946";

    /// <summary>The mark column, wide enough for either glyph and narrow enough to stay a margin.</summary>
    private const int MarkWidth = 18;

    /// <summary>Room for five digits: more lines than a markdown file is ever likely to have.</summary>
    private const int LineNumberWidth = 56;

    /// <summary>How far a row fades once its line numbers can no longer be trusted.</summary>
    private const double StaleOpacity = 0.55;

    private const int DefaultMinimumWidth = 520;
    private const int DefaultMinimumHeight = 320;

    private readonly ExportIssueReport _report;
    private readonly Action<Guid, int> _goToLine;
    private readonly IWorkspaceService _workspace;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<ExportReportWindow> _logger;

    private readonly Grid _root = new();
    private readonly TextBlock _summary = new();
    private readonly StackPanel _staleNotice = new();
    private readonly ListView _list = new();
    private readonly Button _copy = new() { Content = "Copy to clipboard" };
    private readonly Button _close = new() { Content = "Close" };

    private readonly List<FontIcon> _warningGlyphs = [];

    /// <summary>
    /// The documents that have moved on since the export, by id.
    ///
    /// Per document rather than one flag for the whole report, because a Folio names as many
    /// documents as the author ticked. Editing one of twelve dims that one's rows and leaves the
    /// other eleven working - which is the only honest answer, since the other eleven really are
    /// still where the report says they are.
    /// </summary>
    private readonly HashSet<Guid> _staleDocuments = [];

    private bool _isShuttingDown;

    public ExportReportWindow(
        ExportIssueReport report,
        Action<Guid, int> goToLine,
        IWorkspaceService workspace,
        ISettingsService settings,
        IThemeService theme,
        IUiDispatcher ui,
        IntPtr ownerHandle,
        ILogger<ExportReportWindow> logger)
        : base("Export report", DefaultMinimumWidth, DefaultMinimumHeight, settings, theme, ownerHandle, logger)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(goToLine);

        _report = report;
        _goToLine = goToLine;
        _workspace = workspace;
        _settings = settings;
        _theme = theme;
        _ui = ui;
        _logger = logger;

        // A warning, said in the caption as well as on the page: this window opens by itself
        // after an export the user believed had simply worked.
        Title = "Export warnings";

        ConfigurePresenter();
        BuildContent();

        Content = _root;

        ApplyTheme(_theme.Effective);

        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;

        _theme.EffectiveThemeChanged += OnEffectiveThemeChanged;
        _workspace.Changed += OnWorkspaceChanged;

        TrackPlacementChanges();
    }

    /// <summary>Shows the window where it was last left, over the editor the first time.</summary>
    public void Present(RectInt32 nearby)
    {
        RestorePlacement(nearby);

        AppWindow.Show();
        Activate();
    }

    protected override WindowPlacement SavedPlacement => _settings.Current.ExportReportPlacement;

    protected override AppSettings StorePlacement(AppSettings settings, WindowPlacement placement) =>
        settings with { ExportReportWindow = placement };

    // ------------------------------------------------------------------------- content

    private void BuildContent()
    {
        _root.Padding = new Thickness(16, 14, 16, 14);
        _root.RowSpacing = 10;

        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // heading
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // staleness
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // footer

        Place(_root, BuildHeading(), 0);
        Place(_root, BuildStaleNotice(), 1);
        Place(_root, BuildList(), 2);
        Place(_root, BuildFooter(), 3);

        // Escape closes, and so does Enter: there is nothing here to commit, and a reader who
        // has read the list wants the same thing from both keys.
        CommandFooter.WireKeys(_root, () => Close(), () => Close());
    }

    /// <summary>
    /// The heading, which leads with what happened and follows with what is missing from it.
    ///
    /// That order is the whole point. This window only ever appears after a file has been
    /// written, and it used to open with "Unable to export these items" - so a reader's first
    /// conclusion was that nothing had been produced at all. The outcome goes first; the count
    /// goes underneath, where it can be read as a qualification rather than a verdict.
    ///
    /// The glyph follows the same rule. A warning sign for something actually broken, and a
    /// quieter mark when every row is only worth knowing - an iframe that stays on the web is
    /// not a fault, and a triangle beside it says otherwise.
    /// </summary>
    private StackPanel BuildHeading()
    {
        var heading = new StackPanel { Spacing = 4 };

        var title = new TextBlock
        {
            Text = _report.Outcome,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };

        _summary.Opacity = 0.8;
        _summary.TextWrapping = TextWrapping.Wrap;

        FillSummary();

        heading.Children.Add(WithWarningGlyph(title, glyphSize: 18, caution: _report.FailureCount > 0));
        heading.Children.Add(_summary);

        return heading;
    }

    /// <summary>
    /// A warning glyph beside text that has to wrap.
    ///
    /// A Grid rather than a horizontal StackPanel, and that is not a preference. A StackPanel
    /// offers its children unlimited width along its own orientation, so a TextBlock inside
    /// one never reaches the width at which it would wrap: it lays itself out on a single
    /// line and runs off the edge of the window, where it is clipped rather than wrapped. Both
    /// notices in this window were built that way and both were truncated.
    /// </summary>
    /// <param name="caution">
    /// Whether this is a warning. Passed in rather than read from the report, because the two
    /// callers are not asking the same question: the heading reflects whether any row is a real
    /// fault, while the staleness notice is a warning whatever the rows say - line numbers that
    /// have stopped being true are wrong regardless of what they point at.
    /// </param>
    private Grid WithWarningGlyph(FrameworkElement text, double glyphSize, bool caution)
    {
        var row = new Grid { ColumnSpacing = 8 };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Top rather than center: the glyph belongs beside the first line of the text, and a
        // centered one drifts down the block as the text wraps to two lines and three.
        var glyph = new FontIcon
        {
            Glyph = caution ? WarningGlyph : InfoGlyph,
            FontSize = glyphSize,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        };

        // Only a real fault gets the caution color. A report made entirely of things worth
        // knowing is not a warning, and coloring it as one trains the reader to ignore the
        // ones that are.
        if (caution)
        {
            glyph.Foreground = CautionBrush(_theme.Effective);
            _warningGlyphs.Add(glyph);
        }
        else
        {
            glyph.Opacity = 0.7;
        }

        Grid.SetColumn(glyph, 0);
        Grid.SetColumn(text, 1);

        row.Children.Add(glyph);
        row.Children.Add(text);

        return row;
    }

    /// <summary>
    /// The line under the heading: which file was written, from which document, and how much
    /// of it is missing.
    ///
    /// Assembled from runs rather than set as one string so the two names can carry weight.
    /// They are the two things a reader checks first - which document, and which file - and in
    /// a sentence of even-weight text they are the two hardest things in it to find.
    /// </summary>
    private void FillSummary()
    {
        _summary.Inlines.Clear();

        _summary.Inlines.Add(new Run
        {
            Text = Path.GetFileName(_report.OutputPath),
            FontWeight = FontWeights.SemiBold,
        });

        _summary.Inlines.Add(new Run { Text = " was written from " });

        _summary.Inlines.Add(new Run
        {
            Text = _report.DocumentName,
            FontWeight = FontWeights.SemiBold,
        });

        _summary.Inlines.Add(new Run { Text = $". {Tally()}" });
    }

    /// <summary>
    /// What is missing and what is merely worth knowing, counted apart.
    ///
    /// A report of three faults and an iframe used to read "4 items are not in it", which
    /// overstates the first number and misfiles the iframe - it is not missing, it is working
    /// exactly as an iframe works. The two are counted separately here and everywhere else that
    /// says a number, including the text this window copies to the clipboard.
    /// </summary>
    private string Tally()
    {
        int failures = _report.FailureCount;
        int advisories = _report.AdvisoryCount;

        string missing = failures == 1
            ? "1 item could not be included"
            : $"{failures.ToString(CultureInfo.CurrentCulture)} items could not be included";

        string worth = advisories == 1
            ? "1 more is worth knowing about"
            : $"{advisories.ToString(CultureInfo.CurrentCulture)} more are worth knowing about";

        if (failures == 0)
        {
            // Nothing is wrong with it at all, and the reader should not have to work that out
            // by noticing the absence of a complaint.
            return advisories == 1
                ? "Everything came across. One thing below is worth knowing about."
                : $"Everything came across. {advisories.ToString(CultureInfo.CurrentCulture)} things below are worth knowing about.";
        }

        return advisories == 0
            ? $"{missing}. Everything else came across."
            : $"{missing}, and {worth}.";
    }

    /// <summary>
    /// The notice that the line numbers have stopped being true, hidden until it is.
    ///
    /// Deliberately not an offer to re-run anything: re-exporting is the reader's business and
    /// would overwrite a file they may already have sent. All this has to do is stop the list
    /// claiming to know where anything is.
    /// </summary>
    private StackPanel BuildStaleNotice()
    {
        _staleNotice.Visibility = Visibility.Collapsed;

        // Two wordings, because "the document" is a lie about a Folio of twelve - and the rows
        // that faded are the only ones affected, which is worth saying rather than leaving the
        // reader to infer it from the ones that still light up.
        var text = new TextBlock
        {
            Text = _report.ExportedText.Count > 1
                ? "Some of these documents have changed since they were exported, so the faded "
                    + "rows no longer point at the right places. The rest are still good."
                : "The document has changed since it was exported, so these line numbers no "
                    + "longer point at the right places. Export again to bring them up to date.",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Always a warning. Whether any row is a fault has nothing to do with it: the line
        // numbers have stopped being true, and a row that takes the caret to the wrong place is
        // wrong whatever it was trying to say.
        _staleNotice.Children.Add(WithWarningGlyph(text, glyphSize: 14, caution: true));

        return _staleNotice;
    }

    private ListView BuildList()
    {
        _list.SelectionMode = ListViewSelectionMode.Single;
        _list.IsItemClickEnabled = true;
        _list.ItemContainerStyle = MqStyles.ListRow;
        _list.ItemsSource = _report.Issues;

        _list.ContainerContentChanging += OnContainerContentChanging;
        _list.ItemClick += OnItemClick;

        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollMode(_list, ScrollMode.Disabled);

        return _list;
    }

    private StackPanel BuildFooter()
    {
        _copy.Click += (_, _) => CopyToClipboard();
        _close.Click += (_, _) => Close();

        // Close is the commit here: there is nothing to agree to, and dismissing is the only
        // thing this window is finished by. CommandFooter decides the order and the emphasis -
        // see docs/Button-App-Standards.md.
        return CommandFooter.Commit(_close, _copy);
    }

    private static void Place(Grid grid, FrameworkElement element, int row)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }

    // ---------------------------------------------------------------------------- rows

    /// <summary>
    /// Fills each row as it is realised.
    ///
    /// An event rather than an override of <c>PrepareContainerForItemOverride</c>: that
    /// protected virtual is not routed back into a managed subclass of a WinUI control, so it
    /// compiles, never runs, and every row draws itself as the record's own ToString. Find All
    /// learned that one the expensive way; this is the documented hook.
    /// </summary>
    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is not ListViewItem container)
        {
            return;
        }

        if (args.InRecycleQueue)
        {
            container.Content = null;
            return;
        }

        container.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        container.Padding = new Thickness(10, 6, 10, 6);
        container.MinHeight = 0;
        container.ContentTemplate = null;

        if (args.Item is not ExportIssue issue)
        {
            container.Content = null;
            return;
        }

        bool stale = IsStale(issue);

        container.Opacity = stale ? StaleOpacity : 1;
        container.Content = BuildRow(issue);

        ToolTipService.SetToolTip(
            container,
            stale
                ? "The document has changed since the export, so this line number may be wrong"
                : issue.IsAdvisory
                    ? $"Nothing is wrong with this - go to line {issue.Line.ToString(CultureInfo.CurrentCulture)}"
                    : $"Go to line {issue.Line.ToString(CultureInfo.CurrentCulture)}");

        args.Handled = true;
    }

    /// <summary>
    /// One issue: where, what, and what it happened to.
    ///
    /// The line number is a column of its own rather than part of the sentence, so the numbers
    /// line up and the list can be read down. An issue with no line - there are none today, but
    /// the type allows it - simply leaves the column empty rather than inventing a zero.
    /// </summary>
    private Grid BuildRow(ExportIssue issue)
    {
        // Only when the report actually holds both. A Word export's issues are every one of them
        // a fault, and a column of identical amber triangles down the side of it distinguishes
        // nothing - it is decoration that trains the eye to skip the very mark it is there to
        // catch. The marks earn their place exactly when there is something to tell apart.
        bool marked = _report.FailureCount > 0 && _report.AdvisoryCount > 0;

        var grid = new Grid { ColumnSpacing = 10 };

        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = marked ? new GridLength(MarkWidth) : new GridLength(0),
        });

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LineNumberWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // The same distinction the copied text makes with "!" and "-", drawn. Two signals, not
        // one: the glyph carries it on its own, so the row still reads correctly to somebody who
        // cannot tell the amber from the grey. Color only ever emphasizes what the shape says.
        var mark = new FontIcon
        {
            Glyph = issue.IsAdvisory ? InfoGlyph : WarningGlyph,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 3, 0, 0),
            Visibility = marked ? Visibility.Visible : Visibility.Collapsed,
        };

        if (issue.IsAdvisory)
        {
            mark.Opacity = 0.5;
        }
        else
        {
            mark.Foreground = CautionBrush(_theme.Effective);
        }

        var line = new TextBlock
        {
            Text = issue.Line > 0 ? issue.Line.ToString(CultureInfo.CurrentCulture) : string.Empty,
            HorizontalAlignment = HorizontalAlignment.Right,
            FontFamily = new FontFamily("Consolas"),
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var text = new StackPanel { Spacing = 2 };

        text.Children.Add(new TextBlock
        {
            Text = issue.Problem,

            // A third signal, and the quietest - and only where it says something. A fault is
            // worth the extra weight against a note beside it; on a report that is all faults,
            // dropping the weight everywhere would just make the list flatter.
            FontWeight = marked && issue.IsAdvisory ? FontWeights.Normal : FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });

        text.Children.Add(new TextBlock
        {
            Text = issue.Item,
            Opacity = 0.75,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        // Only when a report spans more than one document, which the issue itself says by
        // carrying a name at all. On a Word report every row would say the same thing, and a
        // column that repeats is a column that is read once and then ignored.
        if (!string.IsNullOrEmpty(issue.DocumentName))
        {
            text.Children.Add(new TextBlock
            {
                Text = issue.DocumentName,
                FontSize = 11.5,
                Opacity = 0.55,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        Grid.SetColumn(mark, 0);
        Grid.SetColumn(line, 1);
        Grid.SetColumn(text, 2);

        grid.Children.Add(mark);
        grid.Children.Add(line);
        grid.Children.Add(text);

        return grid;
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ExportIssue issue || issue.Line <= 0)
        {
            return;
        }

        // A stale row does not navigate. The line it names describes a document that no longer
        // exists, and landing the caret on whatever has since moved into that line would be a
        // worse answer than doing nothing. Judged per row: the document this one names may be
        // untouched even when another in the same report has been edited.
        if (IsStale(issue))
        {
            return;
        }

        try
        {
            _goToLine(issue.DocumentId, issue.Line);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not go to line {Line} from the export report.", issue.Line);
        }
    }

    // ----------------------------------------------------------------------- clipboard

    /// <summary>
    /// The whole report as text, in the shape somebody would write it down.
    ///
    /// Everything needed to make sense of it later travels with it - which document, which
    /// file, and when - because the place this is pasted has none of that context, and a bare
    /// list of line numbers a week later belongs to no document in particular.
    /// </summary>
    private void CopyToClipboard()
    {
        var text = new StringBuilder();

        // The outcome, not "warnings". This is pasted into a message to somebody, and the file
        // having been written is the first thing they need to know.
        text.AppendLine(CultureInfo.CurrentCulture, $"Marqora - {_report.Outcome}");
        // "Source" rather than "Document": a Folio's is "3 documents", and "Document: 3
        // documents" reads like a mistake. Works for a single document either way.
        text.AppendLine(CultureInfo.CurrentCulture, $"Source:     {_report.DocumentName}");
        text.AppendLine(CultureInfo.CurrentCulture, $"Written to: {_report.OutputPath}");
        text.AppendLine(
            CultureInfo.CurrentCulture,
            $"Exported:   {DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)}");
        text.AppendLine();

        foreach (ExportIssue issue in _report.Issues)
        {
            string where = issue.Line > 0
                ? $"Line {issue.Line.ToString(CultureInfo.CurrentCulture)}"
                : string.Empty;

            // The line column padded to a fixed width, so the reasons line up in a monospaced
            // window and still read as a list in one that is not. A leading mark tells a fault
            // from a note, which the pasted text has no other way of showing.
            string mark = issue.IsAdvisory ? "-" : "!";

            text.AppendLine(CultureInfo.CurrentCulture, $"{mark} {where,-10} {issue.Problem}");
            text.AppendLine(CultureInfo.CurrentCulture, $"  {string.Empty,-10} {issue.Item}");
        }

        string total = Tally();

        text.AppendLine();
        text.AppendLine(total);

        if (_staleDocuments.Count > 0)
        {
            text.AppendLine();
            text.AppendLine(
                _report.ExportedText.Count > 1
                    ? "Some of these documents were edited after this export, so their line numbers may have moved."
                    : "The document was edited after this export, so the line numbers may have moved.");
        }

        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };

            package.SetText(text.ToString());
            Clipboard.SetContent(package);

            // Said on the button itself rather than in a second notice: the window already
            // carries one warning, and a report about a report is noise. It says so for two
            // seconds and then goes back to being a button.
            _copy.Content = "Copied";

            Microsoft.UI.Dispatching.DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();

            timer.Interval = TimeSpan.FromSeconds(2);
            timer.IsRepeating = false;

            timer.Tick += (sender, _) =>
            {
                sender.Stop();

                if (!_isShuttingDown)
                {
                    _copy.Content = "Copy to clipboard";
                }
            };

            timer.Start();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The export report could not be copied to the clipboard.");
            _copy.Content = "Could not copy";
        }
    }

    // ----------------------------------------------------------------------- staleness

    /// <summary>
    /// Notices when the exported document moves on beneath the report.
    ///
    /// Reference equality is the whole test, as in Find All: an edit allocates a new string,
    /// and an edit that put the text back as it was leaves nothing to say. A closed document
    /// counts too - there is nothing left to navigate into.
    /// </summary>
    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e) =>
        _ui.Post(() => NoteWorkspaceChange(e));

    private void NoteWorkspaceChange(WorkspaceChangedEventArgs e)
    {
        // Only the documents this report actually describes, and only the ones still good. A
        // Folio watches several at once, so "already stale" is asked per document rather than
        // of the report as a whole.
        if (_isShuttingDown
            || _staleDocuments.Contains(e.DocumentId)
            || !_report.ExportedText.TryGetValue(e.DocumentId, out string? exported))
        {
            return;
        }

        switch (e.Change)
        {
            case WorkspaceChange.Closed:
                MarkStale(e.DocumentId);
                break;

            case WorkspaceChange.Edited or WorkspaceChange.ReloadedFromDisk
                when !ReferenceEquals(e.Document?.Text, exported):
                MarkStale(e.DocumentId);
                break;

            default:
                break;
        }
    }

    /// <summary>Whether this row's document has moved on since the export.</summary>
    private bool IsStale(ExportIssue issue) => _staleDocuments.Contains(issue.DocumentId);

    private void MarkStale(Guid documentId)
    {
        _staleDocuments.Add(documentId);
        _staleNotice.Visibility = Visibility.Visible;

        // Redraw the rows so the ones that named this document fade and stop offering to take
        // anybody anywhere. The rest are untouched, because the rest are still true.
        _list.ItemsSource = null;
        _list.ItemsSource = _report.Issues;

        _logger.LogDebug("An export report row went stale: {Document} changed.", documentId);
    }

    // --------------------------------------------------------------------------- chrome

    private void OnEffectiveThemeChanged(object? sender, AppTheme theme) =>
        _ui.Post(() => ApplyTheme(theme));

    /// <summary>
    /// Paints the window in Marqora's theme rather than the operating system's.
    ///
    /// All three are needed, and setting two of them is not a partial fix but an invisible
    /// window: the controls follow <c>RequestedTheme</c>, the page behind them is an explicit
    /// brush, and the caption is painted by Windows rather than by XAML. See
    /// <c>PaletteWindow.SurfaceBrush</c> for why a brush cannot simply be looked up here.
    /// </summary>
    private void ApplyTheme(AppTheme theme)
    {
        _root.RequestedTheme = theme == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        _root.Background = SurfaceBrush(theme);

        foreach (FontIcon glyph in Warnings())
        {
            glyph.Foreground = CautionBrush(theme);
        }

        // The row marks are not in that list and must not be: containers are made and unmade as
        // the list scrolls, so keeping every glyph ever realized would be a list that only grows
        // and mostly points at rows that no longer exist. Rebuilding the rows repaints them, and
        // a theme change is rare enough to afford it.
        if (_list.ItemsSource is not null)
        {
            _list.ItemsSource = null;
            _list.ItemsSource = _report.Issues;
        }

        ApplyTitleBarTheme(theme);
    }

    /// <summary>
    /// The warning glyphs, kept as they are made rather than found again afterwards.
    ///
    /// A theme change has to repaint them, and hunting for them through the visual tree means
    /// the repaint quietly stops working the day somebody nests a panel differently - which is
    /// a thing nobody notices until the window is opened in the other theme.
    /// </summary>
    private IEnumerable<FontIcon> Warnings() => _warningGlyphs;

    /// <summary>
    /// Windows' own caution color, stated rather than looked up - the same amber Find All's
    /// staleness notice wears, and for the same reason: a theme brush resolved in code answers
    /// for the operating system's theme rather than the one chosen in Marqora.
    /// </summary>
    private static SolidColorBrush CautionBrush(AppTheme theme) =>
        new(theme == AppTheme.Dark ? Rgb(0xFC, 0xE1, 0x00) : Rgb(0x9D, 0x5D, 0x00));

    private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (args.DidVisibilityChange && sender.IsVisible)
        {
            EnsureOwned();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _isShuttingDown = true;

        CapturePlacement();

        _workspace.Changed -= OnWorkspaceChanged;
        _theme.EffectiveThemeChanged -= OnEffectiveThemeChanged;
        AppWindow.Changed -= OnAppWindowChanged;
        _list.ContainerContentChanging -= OnContainerContentChanging;
        _list.ItemClick -= OnItemClick;

        _logger.LogDebug("The export report window closed.");
    }
}
