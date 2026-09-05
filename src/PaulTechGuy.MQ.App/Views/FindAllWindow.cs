// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Finding;
using Windows.Graphics;
using Windows.System;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Find All: every match in the active tab or across every open one, listed under the
/// document it came from, with the source pane following whichever result is picked.
///
/// Built in code like the other secondary windows. It is a form rather than a document, so
/// there is no markup worth having: a search box, three switches, a scope and a list.
///
/// Closing it hides it. The results, the term and the place in the list are the whole point
/// of the window, and rebuilding them because it was dismissed would make it a dialog rather
/// than something to work through. <see cref="Shutdown"/> is the only path that truly closes.
///
/// The search runs in C# over the workspace's own copy of each document rather than through
/// the editor, because that copy is already here: every open tab's text is in memory, so
/// searching all of them costs nothing over the bridge. It trails the editor by a debounce
/// interval, which cannot matter — reaching this window and typing a term takes far longer.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "A Window's lifetime belongs to the framework; the search's cancellation "
        + "source is disposed whenever it is replaced and again in Shutdown, which the "
        + "application calls as it exits.")]
public sealed partial class FindAllWindow : PaletteWindow
{
    /// <summary>Narrow enough to sit beside the editor, wide enough for a line of markdown.</summary>
    private const int DefaultMinimumWidth = 520;

    private const int DefaultMinimumHeight = 320;


    /// <summary>How many terms the recent list keeps. A dropdown, not a history file.</summary>
    private const int HistoryLimit = 10;

    /// <summary>
    /// One height and one size of type for the form controls - the term box, the history
    /// button, the scope drop-down and the status row they sit above.
    ///
    /// The two buttons used to be sized from here as well, and that is what let this window
    /// drift: 32 and 14 were being restated privately where the rest of the app was reading
    /// them from App.xaml. They are command buttons now and take MqCommandButtonStyle, whose
    /// floor is the same 32 - MqFormRowHeight - so the row did not move.
    ///
    /// These two stay because the controls above are not buttons and have no shared style to
    /// take. The values are the framework's own for a TextBox and a ComboBox, so a row built
    /// from them is already this tall.
    /// </summary>
    private const int ControlHeight = 32;
    private const double ControlFontSize = 14;

    private const string BeforeFirstSearch = "Results appear here";
    private const string NothingFound = "No matches";

    private readonly IWorkspaceService _workspace;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<FindAllWindow> _logger;

    /// <summary>
    /// The search box.
    ///
    /// An AutoSuggestBox rather than a TextBox, for the list it drops under itself while a
    /// term is being typed: the recent searches that start with what is there so far. The
    /// chevron beside it still shows all of them, unfiltered.
    /// </summary>
    private readonly AutoSuggestBox _term = new();
    private readonly Button _history = new();
    private readonly CheckBox _matchCase = new();
    private readonly CheckBox _wholeWord = new();
    private readonly CheckBox _useRegex = new();
    /// <summary>
    /// The scope picker: a menu, not a combo box.
    ///
    /// A ComboBox marks its selection with WinUI's vertical accent bar, which appears nowhere
    /// else in Marqora. Every other mutually-exclusive choice in the app - the view modes,
    /// the theme - is a ToggleMenuFlyoutItem carrying the tick that Themes/Menus.xaml styles.
    /// This is one too, so there is one way to show what is chosen rather than two.
    /// </summary>
    private readonly DropDownButton _scope = new();
    private readonly ToggleMenuFlyoutItem _scopeAll = new() { Text = "All open tabs" };
    private readonly ToggleMenuFlyoutItem _scopeActive = new() { Text = "Active tab" };

    private readonly TextBlock _summary = new();
    private readonly TextBlock _stale = new();
    private readonly FontIcon _staleGlyph = new() { Glyph = "" };
    private readonly StackPanel _staleNotice = new();
    private readonly Button _clear = new();
    private readonly TextBlock _hint = new();
    private readonly Border _frame = new();
    private readonly FindResultsList _results = new();

    /// <summary>
    /// The text each document held when it was searched, by document.
    ///
    /// The string reference is the version stamp. Documents are immutable records and an edit
    /// allocates a new string, so reference equality answers "has this changed?" exactly,
    /// for nothing.
    /// </summary>
    private readonly Dictionary<Guid, string> _searched = [];

    /// <summary>Searched documents that have since been closed. Their rows go quiet.</summary>
    private readonly HashSet<Guid> _closed = [];

    /// <summary>Cancels the search in flight when a newer one starts.</summary>
    private CancellationTokenSource? _search;

    /// <summary>Set while the list is being refilled, so the rebuild is not read as a pick.</summary>
    private bool _isPopulating;

    /// <summary>Set while the search box is still owed the keyboard. See FocusTerm.</summary>
    private bool _focusPending;

    /// <summary>
    /// The text box inside the search box, once the template has been applied.
    ///
    /// An AutoSuggestBox is a text box in a wrapper and exposes none of it - no SelectAll and
    /// no selection at all. Coming back to the window on a term and finding it selected, ready
    /// to be overtyped, is worth reaching through the template for. See FocusTerm.
    /// </summary>
    private TextBox? _termText;

    private FindScope _selectedScope = FindScope.AllDocuments;

    private bool _isShuttingDown;

    public FindAllWindow(
        IWorkspaceService workspace,
        ISettingsService settings,
        IThemeService theme,
        IUiDispatcher ui,
        IntPtr ownerHandle,
        ILogger<FindAllWindow> logger)
        : base("Find All", DefaultMinimumWidth, DefaultMinimumHeight, settings, theme, ownerHandle, logger)
    {
        _workspace = workspace;
        _settings = settings;
        _theme = theme;
        _ui = ui;
        _logger = logger;

        Title = "Find All";

        Content = BuildContent();

        ConfigurePresenter();

        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += OnClosing;

        _theme.EffectiveThemeChanged += OnEffectiveThemeChanged;
        _workspace.Changed += OnWorkspaceChanged;

        Activated += (_, _) =>
        {
            if (_focusPending)
            {
                FocusTerm();
            }
        };
    }

    /// <summary>Raised when the user picks a result out of the list.</summary>
    public event EventHandler<FindMatchActivatedEventArgs>? MatchActivated;

    /// <summary>
    /// Shows the window and puts the keyboard in the search box.
    ///
    /// Unlike the cheatsheet this one does take focus, because the user has just asked to
    /// type a search term and there is nowhere else for those keystrokes to go.
    /// </summary>
    public void Present(string? seedTerm, RectInt32 nearby)
    {
        RestorePlacement(nearby);

        if (!string.IsNullOrEmpty(seedTerm))
        {
            _term.Text = seedTerm;
        }

        _focusPending = true;

        AppWindow.Show();
        Activate();

        // Asking for focus inline lands on a window that is not the foreground one yet, and
        // quietly does nothing: the window comes up with the caret nowhere and the first
        // thing typed goes into the void. One turn of the queue is usually enough, and
        // Activated catches the times it is not.
        DispatcherQueue.TryEnqueue(FocusTerm);
    }

    // ------------------------------------------------------------------- content

    private Grid BuildContent()
    {
        AppSettings current = _settings.Current;

        var root = new Grid
        {
            Background = SurfaceBrush(_theme.Effective),
            RequestedTheme = _theme.Effective == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light,
            Padding = new Thickness(12),
            RowSpacing = 10,
        };

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        StackPanel query = BuildQuery(current);
        Grid.SetRow(query, 0);
        root.Children.Add(query);

        Grid status = BuildStatus();
        Grid.SetRow(status, 1);
        root.Children.Add(status);

        Border results = BuildResults();
        Grid.SetRow(results, 2);
        root.Children.Add(results);

        AddAccelerators(root);

        return root;
    }

    /// <summary>
    /// The search box on its own line, then the switches, with the scope and the button
    /// together at the right.
    ///
    /// Two grids rather than one with shared columns: shared, the search box's column would
    /// be sized against whichever of the scope and the button was wider, and the box would
    /// give up that much width for nothing.
    /// </summary>
    private StackPanel BuildQuery(AppSettings current)
    {
        var panel = new StackPanel { Spacing = 10 };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _term.PlaceholderText = "Find what";
        _term.Height = ControlHeight;

        // About seven terms before it scrolls. The window can be as short as 320 and the
        // popup is held inside it, so a list left to its own size would be cut off instead.
        _term.MaxSuggestionListHeight = 240;

        // Enter in the box and a term picked out of the list both arrive here, which leaves
        // a KeyDown handler nothing to do.
        _term.QuerySubmitted += OnQuerySubmitted;

        // The first time the window is shown, the tree is not live yet when Present asks for
        // focus, and a box that has not loaded refuses it: the window came up with no
        // caret and the first thing typed went nowhere. This is the moment the box can take
        // it. See FocusTerm for the other two attempts.
        _term.Loaded += (_, _) =>
        {
            if (_focusPending)
            {
                FocusTerm();
            }
        };

        // Catches the box's own clear button as much as a term deleted by hand: emptying the
        // box empties the results, so the two can never disagree about what is being looked
        // at.
        _term.TextChanged += OnTermTextChanged;
        AutomationProperties.SetName(_term, "Find what");
        Place(grid, _term, 0, 0);

        _history.Style = Application.Current.Resources["MqToolButtonStyle"] as Style;
        _history.Height = ControlHeight;
        _history.Content = new FontIcon { Glyph = "", FontSize = 11 };
        _history.Click += (_, _) => ShowHistory();
        ToolTipService.SetToolTip(_history, "Recent searches");
        AutomationProperties.SetName(_history, "Recent searches");
        Place(grid, _history, 0, 1);

        panel.Children.Add(grid);

        var lower = new Grid { ColumnSpacing = 8 };
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lower.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var options = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 18,
            VerticalAlignment = VerticalAlignment.Center,
        };

        options.Children.Add(Option(_matchCase, "Match case", current.FindMatchCase,
            value => _settings.Update(s => s with { FindMatchCase = value })));

        options.Children.Add(Option(_wholeWord, "Whole word", current.FindWholeWord,
            value => _settings.Update(s => s with { FindWholeWord = value })));

        options.Children.Add(Option(_useRegex, "Regular expression", current.FindUseRegex,
            value => _settings.Update(s => s with { FindUseRegex = value })));

        Place(lower, options, 0, 0);

        _selectedScope = current.FindScope;

        _scopeAll.Click += (_, _) => ChooseScope(FindScope.AllDocuments);
        _scopeActive.Click += (_, _) => ChooseScope(FindScope.ActiveDocument);

        var scopes = new MenuFlyout();
        scopes.Items.Add(_scopeAll);
        scopes.Items.Add(_scopeActive);

        _scope.Flyout = scopes;
        _scope.MinWidth = 150;
        _scope.Height = ControlHeight;
        _scope.FontSize = ControlFontSize;
        _scope.HorizontalContentAlignment = HorizontalAlignment.Left;
        ToolTipService.SetToolTip(_scope, "Which documents to search");
        AutomationProperties.SetName(_scope, "Search in");

        ShowScope();

        /*
            The accent one, because it is what this window is for.

            This used to be a plain button, and the comment here used to explain that an accent
            button would come up in the user's Windows accent rather than Marqora's teal, which
            "is the one color in the app that is nobody's choice". Both halves of that have
            since stopped being true: the teal override was removed from App.xaml deliberately,
            and the user's accent is now what the tab strip, the change notice and every dialog
            already wear. There is no longer an accent for a second window's tree to miss.

            Size and type come from the shared style rather than from this window's own
            constants - it is the same button as the one at the foot of Preferences, and it
            should not be a different size because it was written on a different day.
        */
        var find = new Button
        {
            Content = "Find All",
            Style = MqStyles.PrimaryCommandButton,
        };

        find.Click += (_, _) => Search();

        // Sitting together at the right edge: between them they are one control - what to
        // search, and go.
        var action = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = MqStyles.ButtonGroupSpacing,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        action.Children.Add(_scope);
        action.Children.Add(find);

        Place(lower, action, 0, 1);

        panel.Children.Add(lower);

        return panel;
    }

    /// <summary>
    /// The line under the search box: what was found, whether it still holds, and what to do
    /// about it.
    ///
    /// Everything here is at the window's ordinary size. It was a size smaller to begin with,
    /// which read as a footnote - and the one thing on this line that must not read as a
    /// footnote is the notice saying the results no longer match the documents.
    ///
    /// The notice and its buttons sit here rather than over the list because they are a
    /// statement about the results as a whole, and because a control that comes and goes is
    /// better off at the edge of the eye than in the middle of what is being read.
    /// </summary>
    private Grid BuildStatus()
    {
        var grid = new Grid { ColumnSpacing = 10, MinHeight = ControlHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _summary.TextTrimming = TextTrimming.CharacterEllipsis;
        _summary.VerticalAlignment = VerticalAlignment.Center;

        _staleGlyph.FontSize = 15;
        _staleGlyph.VerticalAlignment = VerticalAlignment.Center;

        _stale.Text = "Documents have changed";
        _stale.FontWeight = FontWeights.SemiBold;
        _stale.VerticalAlignment = VerticalAlignment.Center;

        ToolTipService.SetToolTip(_staleNotice, "Search again (F5) to bring the results up to date");

        _staleNotice.Orientation = Orientation.Horizontal;
        _staleNotice.Spacing = 6;
        _staleNotice.VerticalAlignment = VerticalAlignment.Center;
        _staleNotice.Visibility = Visibility.Collapsed;
        _staleNotice.Children.Add(_staleGlyph);
        _staleNotice.Children.Add(_stale);

        ApplyCautionTheme(_theme.Effective);

        // An ordinary command button, with the background one has. It was a quiet toolbar button
        // and was invisible in both themes until the pointer found it. Neutral rather than
        // accent: Find All is what this window commits, and a surface gets one accent.
        //
        // It is the only button on this line. There was a Refresh here as well, and it ran
        // exactly what Find All runs - the same method, off the same controls - so the window
        // offered two buttons for one action and no way to tell them apart. Find All is that
        // one action, F5 is its shortcut, and the notice above says when it is worth pressing.
        _clear.Content = "Clear";
        _clear.Style = MqStyles.CommandButton;
        _clear.Visibility = Visibility.Collapsed;
        _clear.Click += (_, _) => ClearResults();
        ToolTipService.SetToolTip(_clear, "Empty the search box and the results");

        Place(grid, _summary, 0, 0);
        Place(grid, _staleNotice, 0, 1);
        Place(grid, _clear, 0, 2);

        return grid;
    }

    /// <summary>
    /// The results, framed.
    ///
    /// The frame is drawn whether or not there is anything in it, so the window keeps its
    /// shape across a search and the area is visibly reserved rather than reading as the
    /// point where the window ran out of content. The hint sits behind the list and shows
    /// through whenever the list is empty.
    /// </summary>
    private Border BuildResults()
    {
        _results.Highlight = HighlightBrush(_theme.Effective);
        _results.HighlightForeground = HighlightForegroundBrush(_theme.Effective);
        _results.ClosedDocuments = _closed;
        _results.Background = null;
        _results.SelectionChanged += OnResultSelectionChanged;
        _results.KeyDown += OnResultsKeyDown;
        _results.DoubleTapped += (_, args) =>
        {
            args.Handled = true;
            ActivateSelectedMatch(focusEditor: true);
        };

        _hint.Text = BeforeFirstSearch;
        _hint.Opacity = 0.55;
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.TextAlignment = TextAlignment.Center;

        var layers = new Grid();
        layers.Children.Add(_hint);
        layers.Children.Add(_results);

        _frame.Child = layers;
        _frame.BorderThickness = new Thickness(1);
        _frame.CornerRadius = new CornerRadius(4);

        ApplyFrameTheme(_theme.Effective);

        return _frame;
    }

    private static void Place(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static CheckBox Option(CheckBox box, string label, bool initial, Action<bool> persist)
    {
        box.Content = label;
        box.IsChecked = initial;
        box.FontSize = 12.5;
        box.MinWidth = 0;

        // Click rather than Checked and Unchecked: one handler, both directions.
        box.Click += (_, _) => persist(box.IsChecked == true);

        return box;
    }

    private void AddAccelerators(FrameworkElement root)
    {
        root.KeyboardAccelerators.Add(Accelerator(VirtualKey.Escape, VirtualKeyModifiers.None, Dismiss));
        root.KeyboardAccelerators.Add(Accelerator(VirtualKey.F5, VirtualKeyModifiers.None, Search));

        // The shortcut that opened the window brings the keyboard back to the term when the
        // window already has focus, which is what every other search box does.
        root.KeyboardAccelerators.Add(Accelerator(
            VirtualKey.F,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            FocusTerm));
    }

    private static KeyboardAccelerator Accelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action run)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };

        accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            run();
        };

        return accelerator;
    }

    /// <summary>
    /// Puts the keyboard in the search box, and says whether it got there.
    ///
    /// Focus is refused while the window is still coming up, so this keeps asking - from the
    /// dispatcher, from the search box as it loads, and again on activation - until one of
    /// them takes.
    /// </summary>
    private void FocusTerm()
    {
        if (!_term.Focus(FocusState.Programmatic))
        {
            return;
        }

        _focusPending = false;

        // Not before the template has been applied, which is why this is asked for here
        // rather than in the constructor, and kept once it answers.
        _termText ??= InnerTextBox(_term);
        _termText?.SelectAll();
    }

    /// <summary>The first TextBox in a control's template, which for an AutoSuggestBox is
    /// the one being typed into.</summary>
    private static TextBox? InnerTextBox(DependencyObject root)
    {
        int children = VisualTreeHelper.GetChildrenCount(root);

        for (int i = 0; i < children; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);

            if (child is TextBox box)
            {
                return box;
            }

            if (InnerTextBox(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Which scope the picker is on.</summary>
    private FindScope SelectedScope => _selectedScope;

    /// <summary>Records the chosen scope and moves the tick to it.</summary>
    private void ChooseScope(FindScope scope)
    {
        _selectedScope = scope;
        _settings.Update(s => s with { FindScope = scope });
        ShowScope();
    }

    /// <summary>
    /// Puts the tick on the chosen scope and names it on the button.
    ///
    /// Both items are written every time, unconditionally: a ToggleMenuFlyoutItem flips its
    /// own IsChecked the moment it is clicked, so clicking the one already ticked would
    /// otherwise untick it and leave the menu claiming no scope at all.
    /// </summary>
    private void ShowScope()
    {
        bool all = _selectedScope == FindScope.AllDocuments;

        _scopeAll.IsChecked = all;
        _scopeActive.IsChecked = !all;

        _scope.Content = all ? _scopeAll.Text : _scopeActive.Text;
    }

    // -------------------------------------------------------------------- search

    /// <summary>
    /// Runs the search and puts the results on screen.
    ///
    /// async void because every caller is an event: a button, a key and an accelerator. The
    /// failure path is handled here rather than left to the global handler.
    /// </summary>
    private async void Search()
    {
        try
        {
            await SearchAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The Find All search failed.");
            Display(null, "The search could not be completed.", BeforeFirstSearch);
        }
    }

    private async Task SearchAsync()
    {
        // Whatever was running is now answering a question nobody is asking.
        _search?.Cancel();
        _search?.Dispose();
        _search = new CancellationTokenSource();

        CancellationToken cancellation = _search.Token;

        var query = new FindQuery
        {
            Term = _term.Text,
            MatchCase = _matchCase.IsChecked == true,
            WholeWord = _wholeWord.IsChecked == true,
            UseRegex = _useRegex.IsChecked == true,
            Scope = SelectedScope,
        };

        // Nothing typed is not a complaint. The box says what it wants and the results area
        // says where the answer will go; saying it a third time on the status line was just
        // one more thing to read.
        if (string.IsNullOrEmpty(query.Term))
        {
            Display(null, string.Empty, BeforeFirstSearch);
            return;
        }

        List<FindDocument> documents = Gather(query.Scope);

        if (documents.Count == 0)
        {
            Display(null, "There is nothing open to search.", BeforeFirstSearch);
            return;
        }

        Remember(query.Term);

        _summary.Text = "Searching…";

        FindResults results;

        try
        {
            results = await Task
                .Run(() => DocumentFinder.Find(query, documents, cancellation), cancellation)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // A newer search is already on its way, and it owns the summary line now.
            return;
        }

        _logger.LogInformation(
            "Find All: {Matches} matches for {Length} characters across {Documents} documents.",
            results.TotalMatches,
            query.Term.Length,
            documents.Count);

        if (results.Error is { } error)
        {
            Display(null, error, NothingFound);
            return;
        }

        List<FindRow> rows = Rows(results);

        Display(rows, Describe(results), NothingFound, documents);

        if (_settings.Current.FindSelectFirstResult)
        {
            SelectFirstMatch(rows);
        }
    }

    /// <summary>
    /// Picks the first match and hands the list the keyboard, which is what the preference
    /// asks for and what a click on that row would have done anyway.
    ///
    /// The selection is deliberately made outside <see cref="_isPopulating"/>: it is meant to
    /// be read as a pick, so <see cref="OnResultSelectionChanged"/> takes the editor there.
    /// That carries the whole of the behaviour - activating the document the match is in,
    /// splitting the panes if the preview had them to itself, and selecting the match -
    /// rather than opening a second route to the same place that could drift from the first.
    ///
    /// Focus goes to the row's own container rather than to the list, so the arrow keys walk
    /// on from this match rather than from wherever the list last had focus. A container
    /// exists only once the list has been laid out and the list was handed its items a moment
    /// ago, which is what the UpdateLayout is for; the list itself is the fallback for a row
    /// that still has none - virtualisation means one is not guaranteed.
    /// </summary>
    private void SelectFirstMatch(IReadOnlyList<FindRow> rows)
    {
        // The first row is a heading, always: Rows writes one per document before its
        // matches. A heading is not somewhere to be sent, so the first *match* is the target.
        if (rows.FirstOrDefault(row => row is FindMatchRow) is not { } first)
        {
            return;
        }

        _results.SelectedItem = first;
        _results.ScrollIntoView(first);
        _results.UpdateLayout();

        if (_results.ContainerFromItem(first) is Control container)
        {
            container.Focus(FocusState.Programmatic);
        }
        else
        {
            _results.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// A snapshot of what to search. Taken here, on the UI thread, so the background search
    /// works from strings that cannot change under it rather than from the live workspace.
    /// </summary>
    private List<FindDocument> Gather(FindScope scope)
    {
        var documents = new List<FindDocument>();

        if (scope == FindScope.ActiveDocument)
        {
            if (_workspace.Active is { } active)
            {
                documents.Add(Snapshot(active));
            }

            return documents;
        }

        foreach (MarkdownDocument document in _workspace.Documents)
        {
            documents.Add(Snapshot(document));
        }

        return documents;
    }

    private static FindDocument Snapshot(MarkdownDocument document) =>
        new(document.Id, document.DisplayName, document.DisplayPath, document.Text);

    private static List<FindRow> Rows(FindResults results)
    {
        var rows = new List<FindRow>();

        foreach (FindDocumentMatches document in results.Documents)
        {
            rows.Add(new FindHeadingRow(
                document.DocumentId,
                document.Name,
                document.Path,
                document.Matches.Count));

            foreach (FindMatch match in document.Matches)
            {
                rows.Add(new FindMatchRow(document.DocumentId, match));
            }
        }

        return rows;
    }

    private static string Describe(FindResults results)
    {
        if (results.TotalMatches == 0)
        {
            return "No matches.";
        }

        string matches = results.TotalMatches == 1 ? "1 match" : $"{results.TotalMatches} matches";

        string where = results.Documents.Count == 1
            ? results.Documents[0].Name
            : $"{results.Documents.Count} documents";

        return results.Truncated
            ? $"The first {matches} in {where}. Narrow the search to see the rest."
            : $"{matches} in {where}.";
    }

    /// <summary>
    /// Puts rows and a summary on screen together, so the two never disagree, and starts the
    /// staleness watch over again: what is on screen has just been stated afresh.
    ///
    /// <paramref name="searched"/> is what the rows were found in, and null when there are no
    /// rows to go stale.
    /// </summary>
    private void Display(
        IReadOnlyList<FindRow>? rows,
        string summary,
        string hint,
        IReadOnlyList<FindDocument>? searched = null)
    {
        _searched.Clear();
        _closed.Clear();

        foreach (FindDocument document in searched ?? [])
        {
            _searched[document.Id] = document.Text;
        }

        _staleNotice.Visibility = Visibility.Collapsed;

        _isPopulating = true;
        _results.ItemsSource = rows;
        _isPopulating = false;

        bool any = rows is { Count: > 0 };

        // Clear is about a list, so it arrives with one and leaves with it.
        _clear.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

        // The hint shows through the empty list, so it has to go when there are rows over it.
        _hint.Text = hint;
        _hint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        _summary.Text = summary;
    }

    /// <summary>
    /// Empties the box and the list, and hands the keyboard back to the box.
    ///
    /// Reachable two ways on purpose: this button, and the search box's own clear button
    /// through <see cref="OnTermTextChanged"/>. Whichever one is reached for, the term and
    /// the results go together.
    /// </summary>
    private void ClearResults()
    {
        _term.Text = string.Empty;
        Display(null, string.Empty, BeforeFirstSearch);
        FocusTerm();
    }

    private void OnTermTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_term.Text.Length == 0)
        {
            Display(null, string.Empty, BeforeFirstSearch);
        }

        // Only what was typed. Choosing from the list writes the term into the box, and so
        // does the recent-searches menu; reopening the list over either would be answering a
        // question that has just been settled.
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        Suggest(_term.Text);
    }

    /// <summary>
    /// Drops the recent searches that start with what has been typed under the box.
    ///
    /// Every one of them, not the best one: the point of the list is that it can be read and
    /// picked from. Typing on narrows it, Up and Down walk it, Enter takes whichever is
    /// highlighted - or, if none is, whatever is in the box. Nothing is written into the box
    /// until a term is actually chosen, so the letters under the caret are only ever the ones
    /// that were typed there.
    ///
    /// The recent list is newest first and the filter keeps that order, so the term searched
    /// for most recently is the one at the top.
    /// </summary>
    private void Suggest(string typed)
    {
        // An empty box is not a term being typed, and every search ever made is not a
        // suggestion. The chevron is right there for the whole list.
        if (typed.Length == 0)
        {
            _term.IsSuggestionListOpen = false;

            return;
        }

        // Case-insensitively, whatever Match case is set to: the recent list is a memory of
        // what was typed, not a second place for the search options to be applied.
        List<string> matches =
        [
            .. _settings.Current.RecentSearches.Where(recent =>
                recent.StartsWith(typed, StringComparison.OrdinalIgnoreCase)),
        ];

        _term.ItemsSource = matches;
        _term.IsSuggestionListOpen = matches.Count > 0;
    }

    // ----------------------------------------------------------------- staleness

    /// <summary>
    /// Notices when a searched document moves on beneath the results.
    ///
    /// Nothing is re-run and nothing is thrown away. The list stays exactly as it was and
    /// says it may be out of date, because the alternative — reshuffling the rows under the
    /// cursor on every keystroke — would make it useless as a list to work through. Picking a
    /// row that has since moved still lands on the right text; the view model checks before
    /// it selects anything.
    /// </summary>
    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e) =>
        _ui.Post(() => NoteWorkspaceChange(e));

    private void NoteWorkspaceChange(WorkspaceChangedEventArgs e)
    {
        if (!_searched.TryGetValue(e.DocumentId, out string? searched))
        {
            return;
        }

        switch (e.Change)
        {
            case WorkspaceChange.Closed:
                _closed.Add(e.DocumentId);
                Rebuild();
                MarkStale();
                break;

            // Reference equality is the whole test: an edit allocates a new string, and an
            // edit that put the text back as it was leaves nothing to say.
            case WorkspaceChange.Edited or WorkspaceChange.ReloadedFromDisk
                when !ReferenceEquals(e.Document?.Text, searched):
                MarkStale();
                break;

            default:
                break;
        }
    }

    private void MarkStale() => _staleNotice.Visibility = Visibility.Visible;

    private void Remember(string term) =>
        _settings.Update(s => s with
        {
            FindHistory =
            [
                .. new[] { term }
                    .Concat(s.RecentSearches.Where(recent => !string.Equals(recent, term, StringComparison.Ordinal)))
                    .Take(HistoryLimit),
            ],
        });

    private void ShowHistory()
    {
        var flyout = new MenuFlyout();

        foreach (string term in _settings.Current.RecentSearches)
        {
            var item = new MenuFlyoutItem { Text = term };

            item.Click += (_, _) =>
            {
                _term.Text = term;
                Search();
            };

            flyout.Items.Add(item);
        }

        if (flyout.Items.Count == 0)
        {
            flyout.Items.Add(new MenuFlyoutItem { Text = "Nothing searched for yet", IsEnabled = false });
        }

        // Down from the chevron. Left to itself the flyout picks whichever side it has room
        // for, and near the top of a screen that is above the button the arrow points down
        // from - which reads as the wrong menu opening.
        flyout.Placement = FlyoutPlacementMode.BottomEdgeAlignedRight;

        flyout.ShowAt(_history);
    }

    // ------------------------------------------------------------------- picking

    private void OnResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulating)
        {
            return;
        }

        // Moving through the list shows each match without taking the keyboard away from it,
        // so the arrow keys keep working.
        ActivateSelectedMatch(focusEditor: false);
    }

    private void OnResultsKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        ActivateSelectedMatch(focusEditor: true);
    }

    /// <summary>
    /// Enter in the search box, and a term picked out of the list, are the same instruction.
    ///
    /// The term comes from the event, not from the box. An AutoSuggestBox has not finished
    /// writing the picked term into itself when this runs, so reading Text here gives the
    /// letters that were typed before the pick - the very text the pick was meant to replace.
    /// ChosenSuggestion is the picked term, and is null when Enter was pressed without one;
    /// QueryText is what was typed. Between them they always name what was asked for.
    ///
    /// It is written back to the box before the search rather than left to the control, so
    /// that from here on there is one term: the one the box shows, the one searched for and
    /// the one remembered.
    /// </summary>
    private void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // Programmatic, so OnTermTextChanged does not read it as typing and drop the list
        // back open over the results that are about to arrive.
        _term.Text = args.ChosenSuggestion as string ?? args.QueryText;

        Search();
    }

    /// <summary>
    /// Reports the selected match, if the selection is one. A heading is selectable like any
    /// other row and picking one does nothing, which is the right answer for a heading.
    /// </summary>
    private void ActivateSelectedMatch(bool focusEditor)
    {
        if (_results.SelectedItem is not FindMatchRow row)
        {
            return;
        }

        MatchActivated?.Invoke(this, new FindMatchActivatedEventArgs(row.DocumentId, row.Match, focusEditor));
    }

    // -------------------------------------------------------------------- window

    /// <summary>
    /// The tint behind a match in the list.
    ///
    /// Dark is <see cref="MatchColors"/>, the same color the source pane selects a picked
    /// result with, so a match looks the same in the row as it does in the text the row
    /// points at. Light keeps the accent at about a third strength, which is all it has ever
    /// needed, and is the one place that color appears.
    ///
    /// A fixed color rather than a resource lookup, for the same reason
    /// <see cref="SurfaceBrush"/> is.
    /// </summary>
    private static SolidColorBrush HighlightBrush(AppTheme theme) =>
        new(theme == AppTheme.Dark
            ? MatchColors.Background
            : Windows.UI.Color.FromArgb(0x66, 0x51, 0xA8, 0xB1));

    /// <summary>
    /// The text on the tint, in dark mode only: the light tint is translucent and the row's
    /// ordinary color still reads through it, while the dark one is chosen to be seen and
    /// needs text that can be seen on top of it. Null leaves the row as it was.
    /// </summary>
    private static SolidColorBrush? HighlightForegroundBrush(AppTheme theme) =>
        theme == AppTheme.Dark ? new SolidColorBrush(MatchColors.Foreground) : null;

    /// <summary>
    /// Paints the staleness notice in Windows' own caution color - amber, and the color
    /// this app uses for nothing else, so the notice cannot be mistaken for another line of
    /// status. Written out per theme for the reason <see cref="SurfaceBrush"/> gives.
    /// </summary>
    private void ApplyCautionTheme(AppTheme theme)
    {
        var caution = new SolidColorBrush(theme == AppTheme.Dark
            ? Rgb(0xFC, 0xE1, 0x00)
            : Rgb(0x9D, 0x5D, 0x00));

        _staleGlyph.Foreground = caution;
        _stale.Foreground = caution;
    }

    /// <summary>
    /// The frame around the results: a hairline and a surface a shade apart from the page, so
    /// the area reads as somewhere content goes rather than as blank window.
    /// </summary>
    private void ApplyFrameTheme(AppTheme theme)
    {
        bool dark = theme == AppTheme.Dark;

        _frame.Background = new SolidColorBrush(dark ? Rgb(0x2B, 0x2B, 0x2B) : Rgb(0xFF, 0xFF, 0xFF));
        _frame.BorderBrush = new SolidColorBrush(dark ? Rgb(0x3A, 0x3A, 0x3A) : Rgb(0xE1, 0xE1, 0xE1));
    }

    private void OnEffectiveThemeChanged(object? sender, AppTheme theme)
    {
        if (Content is Grid root)
        {
            root.RequestedTheme = theme == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
            root.Background = SurfaceBrush(theme);
        }

        ApplyTitleBarTheme(theme);
        ApplyCautionTheme(theme);
        ApplyFrameTheme(theme);

        _results.Highlight = HighlightBrush(theme);
        _results.HighlightForeground = HighlightForegroundBrush(theme);
        Rebuild();
    }

    /// <summary>
    /// Redraws the rows in place, keeping the selection. The tint is baked into a row when it
    /// is built, so a theme change has to go back over them.
    /// </summary>
    private void Rebuild()
    {
        if (_results.ItemsSource is not { } rows)
        {
            return;
        }

        object? selected = _results.SelectedItem;

        _isPopulating = true;
        _results.ItemsSource = null;
        _results.ItemsSource = rows;
        _results.SelectedItem = selected;
        _isPopulating = false;
    }

    private void Dismiss()
    {
        CapturePlacement();
        AppWindow.Hide();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        args.Cancel = true;
        Dismiss();
    }

    /// <summary>Closes the window for real, as the application exits.</summary>
    public void Shutdown()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;

        CapturePlacement();

        _search?.Cancel();
        _search?.Dispose();
        _search = null;

        _theme.EffectiveThemeChanged -= OnEffectiveThemeChanged;
        _workspace.Changed -= OnWorkspaceChanged;
        AppWindow.Changed -= OnAppWindowChanged;

        Close();
    }

    // ----------------------------------------------------------------- placement

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (args.DidVisibilityChange && sender.IsVisible)
        {
            EnsureOwned();

            // The window is placed before it is shown, and CapturePlacement ignores an
            // invisible window because its bounds are not yet meaningful. Without this, a
            // window the user never moved would have no remembered geometry.
            CapturePlacement();
        }

        if (!args.DidPositionChange && !args.DidSizeChange)
        {
            return;
        }

        CapturePlacement();
    }

    /// <summary>Where Find All was last left. See <see cref="AppSettings.FindAllPlacement"/>.</summary>
    protected override WindowPlacement SavedPlacement => _settings.Current.FindAllPlacement;

    protected override AppSettings StorePlacement(AppSettings settings, WindowPlacement placement) =>
        settings with { FindAllWindow = placement };
}
