// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Controls;
using PaulTechGuy.MQ.Domain;
using Windows.Graphics;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The Folio preflight: what is about to be shared, in what order, and what shape it will take.
///
/// A window rather than a ContentDialog, and that is the whole point of it. A dialog sizes itself
/// to its content and caps itself at a height the framework picks, so a surface carrying a long
/// list <em>and</em> a problems pane <em>and</em> two control rows spends its life fighting for
/// pixels - and every attempt to settle that by arithmetic is a guess at chrome nobody measured.
/// Here the list simply takes the flexible row and the user sizes the window, which is the only
/// arrangement that works for a set that might be three documents or forty.
///
/// Order is why this is a list rather than a set of tick boxes: the reading copy lays the
/// documents out one after another, so the list <em>is</em> the document order, and the rows drag.
///
/// Modeless, like every other window in the app. What that costs is a workspace that can change
/// underneath the plan, and the answer is Find All's: say the plan is out of date and offer to
/// build it again, rather than blocking the editor or quietly sharing a stale set.
/// </summary>
internal sealed class FolioWindow : PaletteWindow
{
    private const int DefaultMinimumWidth = 720;

    private const int DefaultMinimumHeight = 460;

    /// <summary>How tall the problems pane opens at before anyone has dragged it.</summary>
    private const double DefaultProblemsHeight = 150;

    private const double MinimumProblemsHeight = 80;

    /// <summary>Always leave this much for the document list, however hard the splitter is pulled.</summary>
    private const double MinimumListHeight = 140;


    /// <summary>
    /// Past this the recalculation on every tick would be felt. Hashing a few dozen images is
    /// nothing; hashing several hundred on the UI thread is a stutter, so beyond this the
    /// figures wait until the selection settles. The list itself does not care - it virtualizes.
    /// </summary>
    private const int LiveRecalculationLimit = 60;

    /// <summary>
    /// Past this many pictures a single page is slow to open whatever it weighs - a browser has
    /// to lay every one of them out at once, and none of them stream because they are all inline.
    /// It is a layout problem before it is a size problem, which is why it is counted separately.
    /// </summary>
    private const int CrowdedPageImages = 150;

    /// <summary>Past this the one file is unwieldy to open, mail aside.</summary>
    private const long HeavyPageBytes = 50L * 1024 * 1024;

    /// <summary>
    /// What most mail still stops at. Not a reason to refuse anything - plenty of Folios never
    /// go near email - but the number the author is usually judging the total against.
    /// </summary>
    private const long MailLimitBytes = 25L * 1024 * 1024;

    /// <summary>
    /// How much reducible weight is worth mentioning on a Folio that is otherwise fine.
    ///
    /// In bytes rather than a count of images, so that one enormous photograph is noticed and
    /// four slightly-too-wide screenshots are not.
    /// </summary>
    private const long ReducibleBytes = 5L * 1024 * 1024;

    private static readonly string[] Forms =
    [
        "One file others can read without Marqora",
        "A folder of markdown and images",
        "One zip of that folder",
    ];

    /// <summary>
    /// What the order drop-down can be asked for. Both are actions - pick one and the list
    /// re-sorts - which is why "Custom" is not among them: it is not something you can choose,
    /// it is what the order becomes once a row has been dragged, and the box says so by having
    /// nothing selected.
    /// </summary>
    private static readonly string[] Orders = ["The order of the tabs", "Document name (A-Z)"];

    private const int NameOrder = 1;

    private const string CustomOrderCaption = "Custom - dragged";

    private const string UpGlyph = "";

    private const string DownGlyph = "";

    private const string WarningSign = "⚠";

    /// <summary>
    /// For something worth knowing that is not a fault, matching the report window's mark.
    ///
    /// Both windows describe the same warnings - one before the share and one after - so a thing
    /// marked as a note in one and a problem in the other would leave the author working out
    /// which of them to believe.
    /// </summary>
    private const string InfoSign = "ⓘ";

    /// <summary>
    /// The headings the problems pane groups under, in the order they appear.
    ///
    /// Every kind has to be here. The count above the list is the plan's whole warning count,
    /// so a kind missing from this list is counted and never drawn - the pane says "4 problems"
    /// and shows two, which is exactly what happened when the web-picture kinds were added and
    /// this was not. <see cref="OtherProblems"/> is the backstop, so the next one to be added
    /// appears under a dull heading instead of vanishing.
    /// </summary>
    /// <summary>
    /// Faults first, then the things that are only worth knowing. Somebody reading this is
    /// looking for what needs doing, and a list opening with items needing nothing teaches them
    /// to stop reading - the same order the report window uses, for the same reason.
    /// </summary>
    private static readonly (FolioWarningKind Kind, string Heading)[] Groups =
    [
        (FolioWarningKind.MissingImage, "Images that are not there"),
        (FolioWarningKind.RemoteImageFailed, "Pictures that could not be fetched"),
        (FolioWarningKind.OutsideLink, "Links that leave the Folio"),
        (FolioWarningKind.NotRewritable, "References to repoint by hand"),

        (FolioWarningKind.RemoteImageNotIncluded, "Pictures left on the web"),
        (FolioWarningKind.RemoteMediaNotIncluded, "Fetched by whoever opens the Folio"),
        (FolioWarningKind.WillBeShrunk, "Images that will be reduced"),
    ];

    /// <summary>Where a warning kind nobody listed above ends up, rather than nowhere.</summary>
    private const string OtherProblems = "Other problems";

    private static readonly HashSet<FolioWarningKind> Grouped = [.. Groups.Select(g => g.Kind)];

    /// <summary>The width the shrink box opens on, and what most screens are no wider than.</summary>
    private const int DefaultMaxImageWidth = 1600;

    /// <summary>
    /// A drag handle, the tick, the name, then the folder taking whatever is left.
    ///
    /// The name column sizes to its content and is never trimmed; the folder is the one that
    /// gives way, because it is context rather than the thing being looked for. The warning is
    /// a glyph the row supplies as text, so the template needs no converter and no theme lookup.
    /// </summary>
    private const string RowTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
            <Grid ColumnSpacing="8" Padding="0,3,0,3">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>

                <FontIcon Grid.Column="0" Glyph="&#xE700;" FontSize="12" Opacity="0.45"
                          VerticalAlignment="Center" />

                <CheckBox Grid.Column="1" MinWidth="0" Margin="0"
                          IsChecked="{Binding IsIncluded, Mode=TwoWay}"
                          VerticalAlignment="Center" />

                <TextBlock Grid.Column="2" Text="{Binding Name}" VerticalAlignment="Center" />

                <TextBlock Grid.Column="3" Text="{Binding Folder}" Opacity="0.55"
                           TextTrimming="CharacterEllipsis" VerticalAlignment="Center" />

                <TextBlock Grid.Column="4" Text="{Binding WarningGlyph}" VerticalAlignment="Center" />
            </Grid>
        </DataTemplate>
        """;

    private readonly Func<IReadOnlyList<string>> _documents;
    private readonly Func<IReadOnlyList<string>, int, FolioPlan> _plan;
    private readonly IWorkspaceService _workspace;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IUiDispatcher _ui;
    private readonly ILogger _logger;

    private readonly TaskCompletionSource<FolioChoice?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ObservableCollection<FolioDocumentRow> _rows = [];
    private readonly Grid _root = new();
    private readonly ListView _list;
    private readonly ComboBox _form;
    private readonly ComboBox _order;
    private readonly Button _up;
    private readonly Button _down;
    private readonly Button _share;
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _problemsCount = new();
    private readonly StackPanel _warnings = new() { Spacing = 4 };
    private readonly Border _problems;

    /// <summary>
    /// What the preflight says when it has nothing to warn about.
    ///
    /// It takes the problems pane's place rather than leaving a hole, and it is not decoration:
    /// a preflight that shows nothing is indistinguishable from a preflight that has not run,
    /// and the author is about to send this to somebody. "No pictures on the web" is the answer
    /// to a question they would otherwise have to take on trust.
    /// </summary>
    private readonly Border _status;
    private readonly TextBlock _statusTitle = new();
    private readonly StackPanel _statusLines = new() { Spacing = 3 };
    private int _statusRow;

    /// <summary>How to change the order, said once, directly above the list it applies to.</summary>
    private readonly TextBlock _moveHint = new();
    private readonly RowSplitter _splitter;
    private readonly InfoBar _stale;
    private readonly InfoBar _weight;
    private readonly CheckBox _shrink;
    private readonly NumberBox _shrinkWidth;
    private readonly CheckBox _fetch;
    private readonly TextBlock _fetchText;
    private readonly TextBlock _fetchOutcome;
    private readonly InfoBar _fetchBar;
    private readonly Button _useZip;
    private readonly Button _shrinkLarge;

    private IReadOnlyList<FolioDocumentRow> _tabOrder = [];
    private double _problemsHeight = DefaultProblemsHeight;
    private int _splitterRow;
    private int _problemsRow;
    private bool _suppress;
    private bool _isShuttingDown;

    public FolioWindow(
        Func<IReadOnlyList<string>> documents,
        Func<IReadOnlyList<string>, int, FolioPlan> plan,
        IWorkspaceService workspace,
        ISettingsService settings,
        IThemeService theme,
        IUiDispatcher ui,
        IntPtr ownerHandle,
        ILogger<FolioWindow> logger)
        : base("Folio", DefaultMinimumWidth, DefaultMinimumHeight, settings, theme, ownerHandle, logger)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _workspace = workspace;
        _settings = settings;
        _theme = theme;
        _ui = ui;
        _logger = logger;

        Title = "Share as Folio";

        ConfigurePresenter();

        double stored = settings.Current.FolioProblemsHeight;

        _problemsHeight = stored > 0 ? stored : DefaultProblemsHeight;

        _form = DialogFields.Combo(Forms, 0);

        // Which form is chosen decides whether the weight is worth mentioning, so the advice is
        // reconsidered when it changes - including when it changes because the advice was taken.
        _form.SelectionChanged += (_, _) => Refresh();

        _order = DialogFields.Combo(Orders, 0);
        _order.SelectionChanged += (_, _) => Sort();

        _list = new ListView
        {
            ItemsSource = _rows,
            ItemTemplate = XamlReader.Load(RowTemplate) as DataTemplate,

            // Single, not Multiple: the tick in each row says what travels, so selection is free
            // to mean "the row the move buttons act on" - two questions that would otherwise be
            // answered by one gesture, and a multi-select list fights row dragging besides.
            SelectionMode = ListViewSelectionMode.Single,
            CanReorderItems = true,
            CanDragItems = true,
            AllowDrop = true,
            IsItemClickEnabled = false,
            ItemContainerStyle = StretchedRow(),
        };

        _list.SelectionChanged += (_, _) => UpdateMoveButtons();

        AddMoveAccelerator(Windows.System.VirtualKey.Up, -1);
        AddMoveAccelerator(Windows.System.VirtualKey.Down, 1);

        (_up, _down) = BuildMoveButtons();

        _share = new Button { Content = "Share", Style = MqStyles.PrimaryCommandButton };

        _splitter = new RowSplitter { Height = 6, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        _splitter.CurrentHeight = () => _problemsHeight;
        _splitter.HeightChanged += (_, height) => ResizeProblems(height);

        _problems = BuildProblemsPane();
        _status = BuildStatusPanel();

        // A warning, not a note. Everything on screen - the counts, the sizes, the problems -
        // describes a set of documents that is no longer the set that would be shared, and
        // pressing Share anyway produces something other than what is being shown. That is the
        // one state in this window where acting on what you can see gives the wrong answer.
        _stale = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Severity = InfoBarSeverity.Warning,
            Title = "The open documents have changed",
            Message = "This plan was built from the set as it was.",
        };

        var refresh = new Button { Content = "Build it again", Style = MqStyles.CommandButton };

        refresh.Click += (_, _) => Reload();
        _stale.ActionButton = refresh;

        _shrink = new CheckBox { Content = "Shrink images wider than", MinWidth = 0 };
        _shrinkWidth = new NumberBox
        {
            Value = DefaultMaxImageWidth,
            Minimum = 200,
            Maximum = 10_000,
            SmallChange = 100,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            Width = 132,
            IsEnabled = false,
        };

        _shrink.Checked += (_, _) => OnShrinkChanged();
        _shrink.Unchecked += (_, _) => OnShrinkChanged();
        _shrinkWidth.ValueChanged += (_, _) => Refresh();

        // The label is a TextBlock rather than a string because a string handed to Content
        // reaches a presenter with nothing to wrap with, and a narrow window has to be able to
        // wrap this one rather than run it off the edge.
        _fetchText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _fetch = new CheckBox
        {
            Content = _fetchText,
            MinWidth = 0,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };

        // What this choice means, in one paragraph at one weight. It was two blocks for a
        // while, the mechanics set smaller under the outcome, and the split did nothing except
        // invite the second half to be skipped - which is the half about the network.
        _fetchOutcome = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,

            // Indented to sit under the label rather than under the box, so the sentences read
            // as part of the same thought.
            Margin = new Thickness(30, 0, 0, 0),
        };

        _fetch.Checked += (_, _) => OnFetchChanged();
        _fetch.Unchecked += (_, _) => OnFetchChanged();

        /*
            A bar rather than another checkbox row, because it is not another checkbox.

            Everything else in this dialog rearranges what is already on the machine. This one
            decides whether the machine reaches a site, in a product whose whole claim is that
            it does nothing you did not ask for - and it sat between "Shrink images wider than"
            and the splitter, in the same weight, reading like a formatting option.

            Informational rather than Warning: nothing is wrong either way. Ticked or not, the
            bar states the consequence, because the unticked case has one too and is the half
            nobody knew about.
        */
        _fetchBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = false,
            Severity = InfoBarSeverity.Informational,
        };

        _weight = new InfoBar { IsOpen = false, IsClosable = false, Title = "This will be a big file" };

        /*
            Both ways out, rather than only the one.

            A heavy Folio has two answers - send the sources as a zip, or send smaller pictures -
            and which is right depends on what the reader needs, so the dialog offers both rather
            than choosing. Shrinking is the discoverable half: it is off by default and stays off
            by default, but a set that trips this is exactly when somebody would want to know the
            switch exists. In InfoBar.Content rather than as its ActionButton, which holds one.
        */
        _useZip = new Button { Content = "Use a zip instead", Style = MqStyles.CommandButton };
        _shrinkLarge = new Button { Content = "Shrink large images", Style = MqStyles.CommandButton };

        _useZip.Click += (_, _) => _form.SelectedIndex = 2;
        _shrinkLarge.Click += (_, _) => _shrink.IsChecked = true;

        var ways = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = MqStyles.ButtonGroupSpacing,
            Margin = new Thickness(0, 4, 0, 8),
        };

        ways.Children.Add(_useZip);
        ways.Children.Add(_shrinkLarge);

        _weight.Content = ways;

        BuildContent();
        Load();

        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;

        _theme.EffectiveThemeChanged += OnEffectiveThemeChanged;
        _workspace.Changed += OnWorkspaceChanged;
    }

    /// <summary>
    /// What the author settled on, once the window has been answered - null when it was
    /// cancelled or simply closed.
    ///
    /// A task rather than a return value because the window is modeless: nothing blocks while it
    /// is up, so the caller waits on this instead of on a dialog.
    /// </summary>
    public Task<FolioChoice?> Result => _completion.Task;

    /// <summary>Shows the window where it was last left, near the editor the first time.</summary>
    public void Present(RectInt32 nearby)
    {
        RestorePlacement(nearby);

        AppWindow.Show();
        Activate();
    }

    protected override WindowPlacement SavedPlacement => _settings.Current.FolioPlacement;

    protected override AppSettings StorePlacement(AppSettings settings, WindowPlacement placement) =>
        settings with { FolioWindow = placement };

    // ------------------------------------------------------------------ the document set

    /// <summary>
    /// Builds the rows from whatever is open now. Called once on the way up, and again whenever
    /// the author asks for the plan to be rebuilt after the workspace moved under it.
    /// </summary>
    private void Load()
    {
        IReadOnlyList<string> paths = _documents();

        _suppress = true;

        foreach (FolioDocumentRow row in _rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        _rows.Clear();

        bool manyFolders = paths
            .Select(p => Path.GetDirectoryName(p) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;

        foreach (string path in paths)
        {
            var row = new FolioDocumentRow
            {
                Name = Path.GetFileName(path),

                // Carried only when the set spans more than one folder: repeating the same path
                // down twelve rows says nothing and takes the width the names want.
                Folder = manyFolders ? Path.GetDirectoryName(path) ?? string.Empty : string.Empty,
                FullPath = path,
            };

            row.PropertyChanged += OnRowChanged;
            _rows.Add(row);
        }

        _tabOrder = [.. _rows];
        _order.SelectedIndex = 0;
        _list.SelectedIndex = _rows.Count > 0 ? 0 : -1;

        _suppress = false;

        _stale.IsOpen = false;

        UpdateMoveButtons();
        Refresh();
    }

    private void Reload() => Load();

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!_suppress && e.PropertyName == nameof(FolioDocumentRow.IsIncluded))
        {
            Refresh();
        }
    }

    /// <summary>
    /// The ticked documents, in the order the list shows them - which is the order they appear in
    /// the Folio, because the plan names entries as it walks and the writer emits them as the
    /// plan holds them.
    /// </summary>
    private IReadOnlyList<string> Included() =>
        [.. _rows.Where(r => r.IsIncluded).Select(r => r.FullPath)];

    // ------------------------------------------------------------------ order

    private void Sort()
    {
        if (_suppress || _order.SelectedIndex < 0)
        {
            return;
        }

        FolioDocumentRow[] wanted = _order.SelectedIndex == NameOrder
            ? [.. _rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)]
            : [.. _tabOrder];

        _suppress = true;

        for (int i = 0; i < wanted.Length; i++)
        {
            int from = _rows.IndexOf(wanted[i]);

            if (from != i)
            {
                _rows.Move(from, i);
            }
        }

        _suppress = false;

        UpdateMoveButtons();
        Refresh();
    }

    private void OnRowsMoved(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppress || e.Action != NotifyCollectionChangedAction.Move)
        {
            return;
        }

        // Clearing the selection is what puts "Custom - dragged" in the box: there is no such
        // item to select, because dragging is the only way to arrive at it.
        _suppress = true;
        _order.SelectedIndex = -1;
        _suppress = false;

        UpdateMoveButtons();
        Refresh();
    }

    private void Move(int delta)
    {
        int from = _list.SelectedIndex;
        int to = from + delta;

        if (from < 0 || to < 0 || to >= _rows.Count)
        {
            return;
        }

        _rows.Move(from, to);

        // The moved row keeps the selection, so pressing the arrow again carries on with the
        // same document rather than picking up whatever slid into its place.
        _list.SelectedIndex = to;
        _list.ScrollIntoView(_rows[to]);
    }

    private void AddMoveAccelerator(Windows.System.VirtualKey key, int delta)
    {
        var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Key = key,
            Modifiers = Windows.System.VirtualKeyModifiers.Menu,
        };

        accelerator.Invoked += (_, e) =>
        {
            Move(delta);
            e.Handled = true;
        };

        _list.KeyboardAccelerators.Add(accelerator);
    }

    /// <summary>
    /// Grays each arrow out when it has nothing to do - the first row cannot go up, the last
    /// cannot go down. A button that is enabled and does nothing is worse than a dim one.
    /// </summary>
    private void UpdateMoveButtons()
    {
        int selected = _list.SelectedIndex;

        _up.IsEnabled = selected > 0;
        _down.IsEnabled = selected >= 0 && selected < _rows.Count - 1;
    }

    private void SetAll(bool included)
    {
        _suppress = true;

        foreach (FolioDocumentRow row in _rows)
        {
            row.IsIncluded = included;
        }

        _suppress = false;

        Refresh();
    }

    // ------------------------------------------------------------------ the figures

    private void Refresh()
    {
        IReadOnlyList<string> included = Included();

        _share.IsEnabled = included.Count > 0;
        _warnings.Children.Clear();

        foreach (FolioDocumentRow row in _rows)
        {
            row.WarningGlyph = string.Empty;
        }

        if (included.Count == 0)
        {
            _summary.Text = "Nothing selected.";
            _weight.IsOpen = false;

            SetStatusNote(
                "Nothing selected",
                "Tick a document above to see what would travel with it.");

            ShowProblems(false);

            return;
        }

        if (_rows.Count > LiveRecalculationLimit)
        {
            _weight.IsOpen = false;
            _summary.Text =
                $"{Count(included.Count, "document")} selected. "
                + "Too many to total up as you go; the Folio is built from what is ticked.";

            // Not an all-clear. Nothing has been checked at this size, and saying so is the
            // only honest thing the panel can do - the Folio is still built from what is
            // ticked, and it may well carry problems nobody has been shown.
            SetStatusNote(
                "Not checked",
                "There are more documents here than this window totals up as you go, so nothing "
                    + "has been looked over. The Folio still builds from what is ticked.");

            ShowProblems(false);

            return;
        }

        FolioPlan plan = _plan(included, MaxImageWidth);

        _summary.Text = Describe(plan);

        RefreshFetchRow(plan);
        RefreshStatus(plan);
        UpdateWeightAdvice(plan);

        Dictionary<string, FolioDocumentRow> byPath = _rows.ToDictionary(
            r => r.FullPath, r => r, StringComparer.OrdinalIgnoreCase);

        // A document whose only finding is an iframe has nothing wrong with it, and a warning
        // triangle beside its name says otherwise. The mark follows the worst thing found, so a
        // fault anywhere in the document wins over any number of notes.
        foreach (FolioWarning warning in plan.Warnings)
        {
            if (!byPath.TryGetValue(warning.DocumentPath, out FolioDocumentRow? row))
            {
                continue;
            }

            if (!warning.IsAdvisory)
            {
                row.WarningGlyph = WarningSign;
            }
            else if (row.WarningGlyph != WarningSign)
            {
                row.WarningGlyph = InfoSign;
            }
        }

        ShowProblems(plan.Warnings.Count > 0);

        if (plan.Warnings.Count == 0)
        {
            return;
        }

        _problemsCount.Text = ProblemsHeader(plan);

        bool mixed = plan.FailureCount > 0 && plan.AdvisoryCount > 0;

        foreach ((FolioWarningKind kind, string heading) in Groups)
        {
            AddGroup(heading, [.. plan.WarningsOf(kind)], mixed);
        }

        // Anything the list above does not name. Normally empty, and worth the few lines it
        // costs: the count is the plan's whole warning count, so a kind nobody grouped would
        // otherwise be counted and never shown - a pane claiming four problems and listing two.
        FolioWarning[] ungrouped = [.. plan.Warnings.Where(w => !Grouped.Contains(w.Kind))];

        AddGroup(OtherProblems, ungrouped, mixed);
    }

    /// <summary>
    /// The line above the list: how much of this is wrong, and how much is only worth knowing.
    ///
    /// It used to call every warning a problem, which overstated a set where two of four were
    /// the Folio behaving exactly as designed - and contradicted the report window, which says
    /// the same thing properly once the file is written. The two describe the same warnings and
    /// have to agree.
    /// </summary>
    private static string ProblemsHeader(FolioPlan plan)
    {
        int failures = plan.FailureCount;
        int advisories = plan.AdvisoryCount;

        if (failures == 0)
        {
            return $"{InfoSign}  {Count(advisories, "thing")} worth knowing";
        }

        return advisories == 0
            ? $"{WarningSign}  {Count(failures, "problem")}"
            : $"{WarningSign}  {Count(failures, "problem")}, and {Count(advisories, "thing")} worth knowing";
    }

    private void AddGroup(string heading, FolioWarning[] found, bool marked)
    {
        if (found.Length == 0)
        {
            return;
        }

        // Marked only when the pane holds both sorts. A column of identical signs down a list
        // where everything is the same kind distinguishes nothing, and teaches the eye to skip
        // the very mark it is there to catch - the report window draws the line in the same
        // place, for the same reason.
        string mark = marked ? (found[0].IsAdvisory ? InfoSign + "  " : WarningSign + "  ") : string.Empty;

        _warnings.Children.Add(new TextBlock
        {
            Text = $"{mark}{heading} ({found.Length})",
            FontWeight = found[0].IsAdvisory && marked ? FontWeights.Normal : FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, _warnings.Children.Count == 0 ? 0 : 6, 0, 0),
        });

        foreach (FolioWarning warning in found)
        {
            _warnings.Children.Add(new TextBlock
            {
                Text = $"{Path.GetFileName(warning.DocumentPath)}:{warning.Line + 1}  {warning.Message}",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.85,

                // Indented under their heading rather than flush with it. Three levels - the
                // total, the groups, the findings - were all sitting on the same left edge,
                // which left the eye nothing to follow down the pane.
                Margin = new Thickness(marked ? 20 : 14, 1, 0, 0),
            });
        }
    }

    private string Describe(FolioPlan plan)
    {
        var parts = new List<string>
        {
            Count(plan.Documents.Count, "document"),
            Count(plan.Assets.Count, "image"),
        };

        if (plan.RelocatedCount > 0)
        {
            parts.Add($"{plan.RelocatedCount} collected from elsewhere");
        }

        parts.Add(Size(Weight(plan)));

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// What the chosen form will actually weigh.
    ///
    /// The one file is not the sum of its parts: every image goes in as base64, which costs a
    /// third again. Showing the raw total there would understate the file by 33% and make the
    /// figure the author is judging against email useless.
    /// </summary>
    private long Weight(FolioPlan plan) =>
        IsSingleFile ? plan.TotalBytes * 4 / 3 : plan.TotalBytes;

    private bool IsSingleFile => _form.SelectedIndex is 0 or < 0;

    /// <summary>
    /// Says when one page is the wrong shape for this set, and offers the zip in the same breath.
    ///
    /// Two separate ceilings, because they are two different failures. A great many pictures is
    /// a layout problem - the browser lays every one of them out at once and none of them stream,
    /// because they are all inline - and it bites at a count rather than a size. Sheer weight is
    /// the other, and the mail limit below it is advice rather than a fault: plenty of Folios
    /// never go near email.
    /// </summary>
    private void UpdateWeightAdvice(FolioPlan plan)
    {
        // Everything about one page being the wrong shape applies to one page only. Reducible
        // weight is not like that - a photograph that is four times wider than any screen is
        // just as wasteful in a zip - so that last piece of advice is not gated on the form.
        bool single = IsSingleFile;

        long bytes = Weight(plan);
        string zip = Size(plan.TotalBytes);

        _useZip.Visibility = single ? Visibility.Visible : Visibility.Collapsed;

        // Worth offering only when it would actually do something: shrinking is not already on,
        // and something here is wider than the width it would reduce to.
        FolioAsset[] oversized = MaxImageWidth > 0
            ? []
            : [.. plan.Assets.Where(a => a.PixelWidth > DefaultMaxImageWidth)];

        long oversizedBytes = oversized.Sum(a => a.Bytes);

        _shrinkLarge.Visibility = oversized.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        string alsoShrink = oversized.Length > 0
            ? $" {Count(oversized.Length, "image")} here {(oversized.Length == 1 ? "is" : "are")} "
                + $"wider than {DefaultMaxImageWidth} px and could be sent smaller."
            : string.Empty;

        (InfoBarSeverity Severity, string Title, string Message)? advice = (single, plan.Assets.Count) switch
        {
            (true, > CrowdedPageImages) => (
                InfoBarSeverity.Warning,
                "That is a lot of pictures for one page",
                $"{plan.Assets.Count} images in a single file are slow to open, whatever it "
                + $"weighs - a browser lays them all out at once. A zip would be about {zip}."
                + alsoShrink),

            _ when single && bytes > HeavyPageBytes => (
                InfoBarSeverity.Warning,
                "This will be a big file",
                $"About {Size(bytes)} in one page, which is unwieldy to open and past what most "
                + $"mail will take. A zip of the same documents would be about {zip}."
                + alsoShrink),

            _ when single && bytes > MailLimitBytes => (
                InfoBarSeverity.Informational,
                "Larger than most mail will accept",
                $"About {Size(bytes)}, and most mail stops at around 25 MB. It opens perfectly "
                + $"well; it may just need another way to send it. A zip would be about {zip}."
                + alsoShrink),

            /*
                Nothing is wrong with the total, but a lot of it is reducible.

                Without this the offer only ever appears on a Folio that is already in trouble,
                and the case that prompted it - one very large photograph in an otherwise small
                set - never trips any of the thresholds above. Weighed in bytes rather than
                counted, so a handful of slightly-too-wide screenshots stays quiet.
            */
            _ when oversizedBytes > ReducibleBytes && oversized.Length > 0 => (
                InfoBarSeverity.Informational,
                "Some images are larger than they need to be",
                $"{Count(oversized.Length, "image")} account for about {Size(oversizedBytes)} of "
                + $"this, at more than {DefaultMaxImageWidth} px wide - wider than most screens. "
                + "Sending them smaller would not change how they look."),

            _ => null,
        };

        if (advice is not { } shown)
        {
            _weight.IsOpen = false;

            return;
        }

        _weight.Severity = shown.Severity;
        _weight.Title = shown.Title;
        _weight.Message = shown.Message;
        _weight.IsOpen = true;
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    /// <summary>
    /// A size a person reads rather than a byte count. One decimal past a megabyte, because the
    /// question being asked of this number is whether it will go through email.
    /// </summary>
    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB"),
    };

    // ------------------------------------------------------------------ the problems pane

    /// <summary>
    /// Shows or hides the pane and its handle together. No problems means no pane, and the list
    /// takes the height back - which it can do here without anything else moving, because the
    /// window's size is the user's and only the flexible row changes.
    /// </summary>
    private void ShowProblems(bool visible)
    {
        _problems.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _splitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        // The splitter and the panel, by their place in the row order below. Both are zeroed
        // rather than only hidden, so a collapsed pane leaves no gap behind it.
        _root.RowDefinitions[_splitterRow].Height = visible ? GridLength.Auto : new GridLength(0);
        _root.RowDefinitions[_problemsRow].Height = visible ? GridLength.Auto : new GridLength(0);

        // One or the other, never both and never neither. The area under the options always
        // says something, so the window keeps its shape as documents are ticked and unticked -
        // and an author who sees nothing there knows it is because there is nothing to see.
        _status.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        _root.RowDefinitions[_statusRow].Height = visible ? new GridLength(0) : GridLength.Auto;
    }

    /// <summary>
    /// Clamped so the pane can never swallow the list, and never shrink past being readable.
    /// The window is what decides this, not the handle, which is why the handle only reports.
    /// </summary>
    private void ResizeProblems(double height)
    {
        double ceiling = Math.Max(
            MinimumProblemsHeight,
            _root.ActualHeight - MinimumListHeight);

        _problemsHeight = Math.Clamp(height, MinimumProblemsHeight, ceiling);
        _problems.Height = _problemsHeight;

        _settings.Update(s => s with { FolioProblemsHeight = _problemsHeight });
    }

    /// <summary>
    /// How to change the order, said once, above the thing it is about.
    ///
    /// It sits between the "Documents" caption and the list rather than beneath them both,
    /// because it is an instruction for using the list and an instruction is no use arriving
    /// after the fact. Two earlier attempts put it below - one floating in the list's empty
    /// space, one in the row under it - and both had the same defect: the reader met the
    /// control before they met the advice.
    ///
    /// Alt is what the accelerators actually use (VirtualKeyModifiers.Menu). Naming the
    /// modifier is the difference between advice somebody can follow and advice they have to
    /// guess at - and the bare arrow keys move the selection, so "the arrows" was wrong.
    /// </summary>
    private TextBlock BuildReorderHint()
    {
        _moveHint.Text = "Drag a row, or use Alt with the arrow keys, to set the order documents appear in.";
        _moveHint.FontSize = 12;
        _moveHint.Opacity = 0.65;
        _moveHint.TextWrapping = TextWrapping.Wrap;
        _moveHint.Margin = new Thickness(0, -4, 0, 0);

        return _moveHint;
    }

    /// <summary>
    /// What the preflight says when there is nothing wrong: the checks ran, and here is what
    /// they found nothing of.
    ///
    /// Deliberately not a green wall. Only the two facts that carry a decision appear - whether
    /// anything is on the web, and whether anything else is worth reporting - because an empty
    /// state that lists everything it did not find is the thing people rightly complain about.
    /// </summary>
    private Border BuildStatusPanel()
    {
        var stack = new StackPanel { Padding = new Thickness(10, 8, 10, 9), Spacing = 5 };

        _statusTitle.FontWeight = FontWeights.SemiBold;
        _statusTitle.FontSize = 12.5;

        stack.Children.Add(_statusTitle);
        stack.Children.Add(_statusLines);

        return new Border
        {
            Child = stack,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.35 },
        };
    }

    /// <summary>
    /// Fills the resting panel in, one line per thing the author would otherwise have to take
    /// on trust.
    /// </summary>
    private void RefreshStatus(FolioPlan plan)
    {
        _statusTitle.Text = "Ready to share";
        _statusLines.Children.Clear();

        if (plan.RemoteImages.Count == 0)
        {
            _statusLines.Children.Add(StatusLine(
                "No pictures on the web - everything it needs travels inside it", checkedOff: true));
        }

        _statusLines.Children.Add(StatusLine("Nothing else to report", checkedOff: true));
    }

    /// <summary>
    /// The panel when there is nothing to be ready about, or nothing has been checked.
    ///
    /// A separate state rather than an empty version of the other one, and the distinction is
    /// the point: "nothing to report" and "nothing was looked at" are different sentences, and
    /// only one of them is a reason to press Share. A panel that said the first while meaning
    /// the second would be worse than the blank space it replaced.
    /// </summary>
    private void SetStatusNote(string title, string line)
    {
        _statusTitle.Text = title;
        _statusLines.Children.Clear();
        _statusLines.Children.Add(StatusLine(line, checkedOff: false));
    }

    /// <summary>
    /// One line. The tick is only for something actually confirmed - a note about why nothing
    /// has been confirmed does not get to wear one.
    /// </summary>
    private static TextBlock StatusLine(string text, bool checkedOff) => new()
    {
        Text = checkedOff ? "✓  " + text : text,
        FontSize = 12,
        Opacity = 0.75,
        TextWrapping = TextWrapping.Wrap,
    };

    private Border BuildProblemsPane()
    {
        // Bigger, and with a rule under it. Once the groups below started carrying marks of
        // their own, this line had the same glyph, weight and size as the first heading beneath
        // it and read as one more row rather than as the total. A title has to sit apart from
        // what it is counting - the rule does most of that work, and the size does the rest.
        _problemsCount.FontWeight = FontWeights.SemiBold;
        _problemsCount.FontSize = 14;

        var header = new Border
        {
            Child = _problemsCount,
            Padding = new Thickness(10, 7, 10, 8),
            BorderThickness = new Thickness(0, 0, 0, 1),

            // The same gray at the same opacity as the pane's own outline, so the rule reads as
            // part of the frame rather than as a line somebody drew across the middle of it.
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.35 },
        };

        var body = new ScrollViewer
        {
            Content = _warnings,

            // Clear of the rule above, which the old padding of zero left the first heading
            // pressed against.
            Padding = new Thickness(10, 8, 10, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var stack = new Grid();

        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(header, 0);
        Grid.SetRow(body, 1);

        stack.Children.Add(header);
        stack.Children.Add(body);

        return new Border
        {
            Child = stack,
            Height = _problemsHeight,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.35 },
        };
    }

    // ------------------------------------------------------------------ layout

    private void BuildContent()
    {
        _root.Padding = new Thickness(16, 14, 16, 14);
        _root.RowSpacing = 10;

        ApplyTheme(_theme.Effective);

        FrameworkElement[] children =
        [
            DialogFields.Labelled("Share as", _form),
            BuildListHeader(),
            BuildReorderHint(),
            _list,
            BuildMoveRow(),
            BuildShrinkRow(),
            BuildFetchRow(),
            _splitter,
            _problems,
            _status,
            _stale,
            _weight,
            BuildFooter(),
        ];

        /*
            The rows are derived from the children rather than listed beside them, and the two
            that get collapsed are found by looking rather than by counting.

            Both because the alternative was already wrong once: a row inserted in the middle
            left the height list one short and pointed the collapse at the wrong two rows, and
            neither mistake is visible in a diff.

            Only the list is flexible. Everything else is exactly as tall as its content, so the
            list is what gives and takes as the window is resized or the problems pane opens.
        */
        for (int i = 0; i < children.Length; i++)
        {
            _root.RowDefinitions.Add(new RowDefinition
            {
                Height = children[i] == _list ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });

            Grid.SetRow(children[i], i);
            _root.Children.Add(children[i]);
        }

        _splitterRow = Array.IndexOf(children, _splitter);
        _problemsRow = Array.IndexOf(children, _problems);
        _statusRow = Array.IndexOf(children, _status);

        _rows.CollectionChanged += OnRowsMoved;

        CommandFooter.WireKeys(_root, onEnter: Commit, onEscape: Close);

        Content = _root;
    }

    private Grid BuildListHeader()
    {
        var header = new Grid();

        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var caption = new TextBlock
        {
            Text = "Documents",
            FontSize = 12.5,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = MqStyles.ButtonGroupSpacing,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        // Captioned, because a bare drop-down reading "The order of the tabs" beside two buttons
        // reads as a status rather than as something that sorts anything.
        var orderCaption = new TextBlock
        {
            Text = "Order",
            FontSize = 12.5,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
        };

        _order.Width = 185;
        _order.PlaceholderText = CustomOrderCaption;

        ToolTipService.SetToolTip(_order, "How the documents below are ordered. Drag a row to arrange them yourself.");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_order, "Order documents by");

        var all = new Button { Content = "Select all", Style = MqStyles.CommandButton };
        var none = new Button { Content = "Clear all", Style = MqStyles.CommandButton };

        all.Click += (_, _) => SetAll(true);
        none.Click += (_, _) => SetAll(false);

        actions.Children.Add(orderCaption);
        actions.Children.Add(_order);
        actions.Children.Add(all);
        actions.Children.Add(none);

        Grid.SetColumn(caption, 0);
        Grid.SetColumn(actions, 1);

        header.Children.Add(caption);
        header.Children.Add(actions);

        return header;
    }

    private Grid BuildMoveRow()
    {
        var line = new Grid();

        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Buttons only. The advice they are about is above the list, where somebody reads it
        // before using the thing rather than after.
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = MqStyles.ButtonGroupSpacing,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        buttons.Children.Add(_up);
        buttons.Children.Add(_down);

        Grid.SetColumn(buttons, 1);

        line.Children.Add(buttons);

        return line;
    }

    /// <summary>
    /// The one control here that loses something, so it says what it costs.
    ///
    /// Off by default: a Folio hands back whatever went into it, so a reduced picture is reduced
    /// for good, and the conservative setting is the one that leaves the author's images alone.
    /// Which images it would touch is named in the problems panel, beside the missing ones -
    /// this is a thing worth seeing before it happens, not after.
    /// </summary>
    private Grid BuildShrinkRow()
    {
        // A Grid rather than a horizontal StackPanel, and the difference is not cosmetic: a
        // horizontal StackPanel measures its children with unbounded width, so the explanation
        // on the end never meets an edge to wrap at and runs off the window however wide the
        // TextBlock says it wraps. The star column is what gives it a width to wrap inside.
        var row = new Grid { ColumnSpacing = 8 };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _shrink.VerticalAlignment = VerticalAlignment.Center;
        _shrinkWidth.VerticalAlignment = VerticalAlignment.Center;

        var note = new TextBlock
        {
            Text = "px.  A photo already saved as a JPEG may not get smaller, and is left alone when it would not.",
            FontSize = 12,
            Opacity = 0.65,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(_shrink, 0);
        Grid.SetColumn(_shrinkWidth, 1);
        Grid.SetColumn(note, 2);

        row.Children.Add(_shrink);
        row.Children.Add(_shrinkWidth);
        row.Children.Add(note);

        return row;
    }

    /// <summary>
    /// The pictures this Folio names by web address, and what happens to them.
    ///
    /// Hidden entirely when there are none, which is most Folios - an option about a situation
    /// that does not apply is noise, and this dialog already asks the author enough questions.
    /// That is also why a third bar in this window is affordable: it is never on screen beside
    /// the other two unless a document really does point at a picture on the web.
    ///
    /// Off by default, and not remembered between shares. Marqora does not go to the network
    /// unless somebody standing here, reading which sites are involved, asks it to; a setting
    /// that persisted would turn that decision into a mode, which is the thing it must not be.
    ///
    /// The unchecked text is the half that earns this feature. Leaving the addresses alone has
    /// never meant nothing is fetched - it means the reader's browser does the fetching, from
    /// sites they never saw named. That was always true and the app never said so.
    /// </summary>
    private InfoBar BuildFetchRow()
    {
        // Both go into a vertical panel. A horizontal StackPanel was the first attempt and it
        // cannot work: it measures its children with unbounded width, so a TextBlock inside one
        // never reaches the edge it would wrap at and simply runs off the window. Stacked
        // vertically, each child is handed the panel's width and wraps.
        var inside = new StackPanel
        {
            Spacing = 4,

            // An InfoBar gives its content no room of its own; without the bottom margin the
            // last line sits hard against the border.
            Margin = new Thickness(0, 4, 0, 12),
        };

        inside.Children.Add(_fetch);
        inside.Children.Add(_fetchOutcome);

        _fetchBar.Content = inside;

        return _fetchBar;
    }

    /// <summary>
    /// Says how many pictures are on the web and which sites they sit on, before the author has
    /// decided anything - naming them afterwards would be asking about something already done.
    ///
    /// The count and the sites are the bar's own title and message, so they are read before the
    /// checkbox rather than through it: the box then says one short thing, which is what it is
    /// being asked to decide.
    /// </summary>
    private void RefreshFetchRow(FolioPlan plan)
    {
        if (plan.RemoteImages.Count == 0)
        {
            _fetchBar.IsOpen = false;

            // Unticked as it goes, so a set that no longer has any cannot leave the answer
            // behind for a set that does.
            _fetch.IsChecked = false;

            return;
        }

        int count = plan.RemoteImages.Count;
        IReadOnlyList<string> hosts = plan.RemoteHosts;
        bool one = count == 1;

        string pictures = one ? "1 picture in this Folio is" : $"{count} pictures in this Folio are";

        // Named in full up to a point, because "3 sites" tells the author nothing they can weigh.
        string named = hosts.Count <= 3
            ? string.Join(", ", hosts)
            : string.Join(", ", hosts.Take(3)) + $" and {hosts.Count - 3} more";

        // The count of sites is left out when there is one, because the line underneath names
        // it - "from 1 site: commons.wikimedia.org" says the same thing twice.
        _fetchBar.Title = hosts.Count == 1
            ? $"{pictures} on the web"
            : $"{pictures} on the web, from {hosts.Count} sites";

        _fetchBar.Message = named;

        _fetchText.Text = one
            ? "Fetch it now and carry it inside the Folio"
            : "Fetch them now and carry them inside the Folio";

        // Each state says what it means and what it costs in the same breath. Ticked, that is
        // the complete file first and the sites contacted second; unticked, what the reader's
        // browser ends up doing - the surprise - and then that Marqora itself stays put.
        if (_fetch.IsChecked == true)
        {
            _fetchOutcome.Text = "If checked, the Folio will open complete on any machine, with "
                + "nothing left for the reader's browser to go and get. Marqora contacts "
                + $"{(hosts.Count == 1 ? "that site" : "those sites")} while the Folio is built, "
                + "and nothing else. Your answer here is not remembered for the next share.";
        }
        else
        {
            _fetchOutcome.Text = one
                ? "If unchecked, the address travels as written. Whoever opens this Folio has "
                    + "their browser fetch the picture from that site, and sees a hole if it "
                    + "has moved. Marqora itself contacts nothing while the Folio is built."
                : "If unchecked, the addresses travel as written. Whoever opens this Folio has "
                    + "their browser fetch the pictures from those sites, and sees holes where "
                    + "any have moved. Marqora itself contacts nothing while the Folio is built.";
        }

        _fetchBar.IsOpen = true;
    }

    private void OnFetchChanged() => Refresh();

    private (Button Up, Button Down) BuildMoveButtons()
    {
        var up = new Button { Content = new FontIcon { Glyph = UpGlyph }, Style = MqStyles.IconButton };
        var down = new Button { Content = new FontIcon { Glyph = DownGlyph }, Style = MqStyles.IconButton };

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(up, "Move up");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(down, "Move down");

        ToolTipService.SetToolTip(up, "Move up (Alt+Up)");
        ToolTipService.SetToolTip(down, "Move down (Alt+Down)");

        up.Click += (_, _) => Move(-1);
        down.Click += (_, _) => Move(1);

        return (up, down);
    }

    /// <summary>
    /// The totals on the left, the action row on the right. The row itself comes from
    /// <see cref="CommandFooter"/>, which owns the order and which button carries the accent.
    /// </summary>
    private Grid BuildFooter()
    {
        var footer = new Grid();

        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _summary.FontWeight = FontWeights.SemiBold;
        _summary.FontSize = 13;
        _summary.VerticalAlignment = VerticalAlignment.Center;

        var cancel = new Button { Content = "Cancel", Style = MqStyles.CommandButton };

        cancel.Click += (_, _) => Close();
        _share.Click += (_, _) => Commit();

        StackPanel actions = CommandFooter.Commit(_share, cancel);

        Grid.SetColumn(_summary, 0);
        Grid.SetColumn(actions, 1);

        footer.Children.Add(_summary);
        footer.Children.Add(actions);

        return footer;
    }

    private static Style StretchedRow()
    {
        var style = new Style(typeof(ListViewItem));

        style.Setters.Add(new Setter(
            Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

        return style;
    }

    // ------------------------------------------------------------------ lifetime

    private void Commit()
    {
        if (Included().Count == 0)
        {
            return;
        }

        _completion.TrySetResult(new FolioChoice
        {
            DocumentPaths = Included(),
            Form = _form.SelectedIndex switch
            {
                1 => FolioForm.Folder,
                2 => FolioForm.Zip,
                _ => FolioForm.SingleFile,
            },
            MaxImageWidth = MaxImageWidth,
            FetchRemoteImages = _fetch.IsChecked == true,
        });

        Close();
    }

    /// <summary>The width cap in force, or zero when images travel exactly as they are.</summary>
    private int MaxImageWidth =>
        _shrink.IsChecked == true && !double.IsNaN(_shrinkWidth.Value)
            ? (int)_shrinkWidth.Value
            : 0;

    private void OnShrinkChanged()
    {
        _shrinkWidth.IsEnabled = _shrink.IsChecked == true;

        Refresh();
    }

    /// <summary>
    /// The workspace moved while the window was up. Nothing is rebuilt underneath the author -
    /// the list stays exactly as they arranged it and the notice offers to build it again, which
    /// is the same trade Find All makes for the same reason.
    /// </summary>
    private void OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs e) =>
        _ui.Post(() =>
        {
            if (!_isShuttingDown)
            {
                _stale.IsOpen = true;
            }
        });

    private void OnEffectiveThemeChanged(object? sender, AppTheme theme) =>
        _ui.Post(() => ApplyTheme(theme));

    /// <summary>
    /// Paints the window in Marqora's theme rather than the operating system's.
    ///
    /// A Window has no <c>RequestedTheme</c> of its own, so the content root wears it, and all
    /// three of these are needed - the same three the preferences window sets, for the same
    /// reason. The controls and their text follow <c>RequestedTheme</c>; the page behind them is
    /// an explicit brush; the caption is painted by Windows rather than by XAML.
    ///
    /// Setting only two of the three is not a partial fix but an invisible window: the
    /// background arrives in Marqora's theme while every control still resolves against the
    /// application's - which is the operating system's - so a dark desktop with Marqora set to
    /// light draws white text on a light surface.
    /// </summary>
    private void ApplyTheme(AppTheme theme)
    {
        _root.RequestedTheme = theme == AppTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        _root.Background = SurfaceBrush(theme);

        ApplyTitleBarTheme(theme);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_isShuttingDown)
        {
            return;
        }

        if (args.DidVisibilityChange && sender.IsVisible)
        {
            EnsureOwned();

            // The window is placed before it is shown, and CapturePlacement ignores an invisible
            // window because its bounds are not yet meaningful. Without this, a window the user
            // never moved would have no remembered geometry.
            CapturePlacement();
        }

        if (args.DidPositionChange || args.DidSizeChange)
        {
            CapturePlacement();
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _isShuttingDown = true;

        CapturePlacement();

        _workspace.Changed -= OnWorkspaceChanged;
        _theme.EffectiveThemeChanged -= OnEffectiveThemeChanged;
        AppWindow.Changed -= OnAppWindowChanged;
        _rows.CollectionChanged -= OnRowsMoved;

        foreach (FolioDocumentRow row in _rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        // Closing without answering is a cancel. Harmless when Commit already set a result.
        _completion.TrySetResult(null);

        _logger.LogDebug("The Folio window closed.");
    }
}
