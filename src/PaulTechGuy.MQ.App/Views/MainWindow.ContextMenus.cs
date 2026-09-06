// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;
using Windows.Foundation;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The right-click menus for the two panes.
///
/// Both are ordinary WinUI MenuFlyouts, which is the whole point of them. The panes are web
/// content, and each used to bring its own menu: Monaco drew one in the DOM for the source
/// pane and Chromium drew a native one for the preview. Three menus from three toolkits
/// could never be made to match, and the preview's followed Edge's dark mode rather than
/// the app's theme, so it came up dark in a light window. Both are switched off now. The
/// panes report the click across the bridge and these go up instead, styled from
/// Themes/Menus.xaml along with the header menu bar.
///
/// Built in code rather than declared in MainWindow.xaml because a WinUI resource
/// dictionary will not take x:Name, so a declared menu's items could not be reached to
/// enable and hide them per click - which is most of what the code below does. The
/// recent-files submenu is built the same way, a few hundred lines further up.
///
/// They are built once and kept. A context menu is opened often, and rebuilding one every
/// time would throw away the flyout the framework has already measured.
///
/// The third right-click menu in the window is the tab strip's, in
/// MainWindow.TabContextMenu.cs. It is kept apart because it is not one of these: it is
/// raised by a XAML event rather than reported over the bridge, and it is fitted to the tab
/// that was clicked rather than to what the pointer was over.
/// </summary>
public sealed partial class MainWindow
{
    private MenuFlyout? _sourceMenu;
    private MenuFlyout? _previewMenu;

    // Preview items that appear only when the pointer was over something they apply to.
    private MenuFlyoutItem? _copyLinkItem;
    private MenuFlyoutItem? _copyImageItem;
    private MenuFlyoutSeparator? _targetSeparator;

    // Items that need a selection to mean anything. The two Copy items are separate
    // because they copy from different places: the editor's selection and the preview's.
    private MenuFlyoutItem? _cutItem;
    private MenuFlyoutItem? _sourceCopyItem;
    private MenuFlyoutItem? _previewCopyItem;

    // Items that need a document with something in it.
    private readonly List<MenuFlyoutItem> _contentItems = [];

    /// <summary>
    /// The spelling suggestions, built once and relabelled per click.
    ///
    /// A fixed set of slots rather than items created and destroyed each time, because this file
    /// keeps its menus rather than rebuilding them - see the note at the top. There are always
    /// AppSettings.MaximumSpellSuggestionCount of them and the click shows as many as it has
    /// words for, so changing the setting needs no rebuild.
    ///
    /// Deliberately not in _contentItems: those are enabled by whether the document has any text,
    /// and these are shown by whether the pointer was on a misspelling. Putting them in both
    /// lists would have the two rules fighting.
    /// </summary>
    private MenuFlyoutItem[]? _suggestionItems;
    private MenuFlyoutItem? _noSuggestionsItem;
    private MenuFlyoutItem? _deleteRepeatedItem;
    private MenuFlyoutItem? _addToDictionaryItem;
    private MenuFlyoutSeparator? _spellingSeparator;

    /// <summary>The misspelling that was right-clicked, captured for the item handlers.</summary>
    private SpellingHit? _clickedSpelling;

    /// <summary>
    /// The replacements offered for a dead link, built once and relabelled per click - the same
    /// arrangement as the spelling suggestions above, and for the same reason.
    /// </summary>
    private MenuFlyoutItem[]? _linkSuggestionItems;

    private MenuFlyoutItem? _removeLinkItem;

    private MenuFlyoutSeparator? _linkSeparator;

    /// <summary>Shown only when the clipboard is actually carrying a picture.</summary>
    private MenuFlyoutItem? _pasteImageItem;

    /// <summary>The dead link that was right-clicked, captured for the item handlers.</summary>
    private LinkFindingHit? _clickedLink;

    /// <summary>What the pointer was over, captured at the click and used by the two Copy items.</summary>
    private string? _clickedLinkUrl;
    private string? _clickedImageUrl;

    /// <summary>
    /// A right-click arrived from one of the panes. Fit the menu to what was under the
    /// pointer, then put it where the pointer is.
    /// </summary>
    private void OnContextMenuRequested(object? sender, PaneContextMenuEventArgs e)
    {
        // Zoom and scroll commands act on the pane you were last in, and right-clicking a
        // pane is being in it.
        ViewModel.SetActivePane(e.Pane);

        _clickedLinkUrl = e.LinkUrl;
        _clickedImageUrl = e.ImageUrl;
        _clickedSpelling = e.Spelling;
        _clickedLink = e.LinkFinding;

        MenuFlyout menu = e.Pane == EditorPane.Source
            ? BuildSourceMenu()
            : BuildPreviewMenu();

        foreach (MenuFlyoutItem item in _contentItems)
        {
            item.IsEnabled = ViewModel.HasContent;
        }

        if (e.Pane == EditorPane.Source)
        {
            if (_cutItem is not null) { _cutItem.IsEnabled = e.HasSelection; }
            if (_sourceCopyItem is not null) { _sourceCopyItem.IsEnabled = e.HasSelection; }

            FitSpellingItems(e.Spelling);
            FitLinkItems(e.LinkFinding);

            // Collapsed rather than disabled, for the same reason the preview's Copy Link is:
            // an item that can never do anything on this clipboard is not worth a greyed row.
            Show(_pasteImageItem, ViewModel.ClipboardHasImage);
        }
        else
        {
            // Collapsed rather than disabled: a menu that always carries Copy Link is
            // mostly a menu about links, and the preview's is not.
            Show(_copyLinkItem, e.LinkUrl is not null);
            Show(_copyImageItem, e.ImageUrl is not null);
            Show(_targetSeparator, e.LinkUrl is not null || e.ImageUrl is not null);

            if (_previewCopyItem is not null) { _previewCopyItem.IsEnabled = e.HasSelection; }
        }

        // Anchored on the panel rather than the WebView inside it. They occupy the same
        // rectangle, so the coordinates are the same either way, and the panel is the one
        // that survives the WebView being replaced after a crash.
        menu.ShowAt(PreviewSurface, new FlyoutShowOptions
        {
            // In the WebView's own coordinates, which is what the pane reported.
            Position = new Point(e.X, e.Y),
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,

            // Standard rather than Transient, so the menu takes focus and can be walked
            // with the arrow keys the way the header menus can.
            ShowMode = FlyoutShowMode.Standard,
        });
    }

    /// <summary>Collapses or reveals one item. Typed for the base so it takes separators too.</summary>
    private static void Show(MenuFlyoutItemBase? item, bool visible)
    {
        if (item is not null)
        {
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Fits the spelling block to the word under the pointer, or hides it entirely.
    ///
    /// Nothing is created or destroyed here: the slots were built with the menu and this only
    /// relabels them, which is what keeps the promise made at the top of this file.
    ///
    /// The suggestions themselves are fetched now rather than in advance. It is one call to the
    /// spell engine, on the UI thread, in the moment between the click and the menu appearing -
    /// a few milliseconds the first time a given word is asked about and nothing at all
    /// afterwards, because the analyzer caches by word. Computing them for every misspelling as
    /// the document was checked would mean hundreds of calls to fill a menu opened once.
    /// </summary>
    private void FitSpellingItems(SpellingHit? spelling)
    {
        if (_suggestionItems is null)
        {
            return;
        }

        bool repeated = spelling is { Repeated: true };

        // A repeated word has nothing to suggest: the fix is to delete it, not to respell it.
        IReadOnlyList<string> words = spelling is { } hit && !repeated
            ? ViewModel.SuggestionsFor(hit)
            : [];

        int offered = Math.Min(words.Count, Math.Clamp(
            ViewModel.SpellSuggestionCount,
            AppSettings.MinimumSpellSuggestionCount,
            _suggestionItems.Length));

        for (int i = 0; i < _suggestionItems.Length; i++)
        {
            bool used = i < offered;

            if (used)
            {
                _suggestionItems[i].Text = words[i];
            }

            Show(_suggestionItems[i], used);
        }

        Show(_noSuggestionsItem, spelling is not null && !repeated && offered == 0);
        Show(_deleteRepeatedItem, repeated);

        // Offered for a misspelling but not for a repeat: "has" is already a word, and adding it
        // would teach the dictionary nothing.
        Show(_addToDictionaryItem, spelling is not null && !repeated);
        Show(_spellingSeparator, spelling is not null);
    }

    /// <summary>
    /// Fits the dead-link block to whatever is under the pointer, or hides it entirely.
    ///
    /// Same arrangement as the spelling block: nothing is created or destroyed, the slots are
    /// relabelled, and the suggestions are fetched now rather than in advance because working
    /// them out costs a folder listing.
    ///
    /// Browsing for a replacement image and pasting one from the clipboard both belong here too.
    /// Neither is wired yet: they need the asset store that writes a file beside the document
    /// and rewrites the path, which does not exist until image paste lands.
    /// </summary>
    private void FitLinkItems(LinkFindingHit? finding)
    {
        if (_linkSuggestionItems is null)
        {
            return;
        }

        // Missing alt text has nothing to put on a menu. There is no other file it might have
        // meant, and removing a perfectly good image because nobody has described it yet would
        // be the wrong answer to the question. The hover says what is missing; typing fixes it.
        if (finding is { Kind: LinkFindingKind.MissingAltText })
        {
            finding = null;
        }

        IReadOnlyList<string> targets = finding is { } hit ? ViewModel.SuggestionsFor(hit) : [];
        int offered = Math.Min(targets.Count, _linkSuggestionItems.Length);

        for (int i = 0; i < _linkSuggestionItems.Length; i++)
        {
            bool used = i < offered;

            if (used)
            {
                // The target is what gets written, but the menu says what the reader will see
                // happen. "Did you mean" rather than a bare path, so the item reads as an offer
                // rather than as a fact.
                _linkSuggestionItems[i].Text = $"Did you mean \"{targets[i]}\"?";
                _linkSuggestionItems[i].Tag = targets[i];
            }

            Show(_linkSuggestionItems[i], used);
        }

        // Worded for what it removes. An image and a link are the same construct to the parser
        // and a different thing entirely to the person looking at the screen.
        if (_removeLinkItem is not null && finding is { } removable)
        {
            _removeLinkItem.Text = removable.Kind == LinkFindingKind.MissingImage
                ? "Remove this image"
                : "Remove this link";
        }

        // A dead anchor is not removed from here. The heading it wanted usually exists under
        // another name, and if it truly does not the sentence around the link needs rewriting
        // rather than the link deleting.
        Show(_removeLinkItem, finding is { Kind: not LinkFindingKind.DeadAnchor });
        Show(_linkSeparator, finding is not null);
    }

    /// <summary>
    /// Points a dead link at one of the suggestions.
    ///
    /// The target comes off the item's Tag rather than being captured when the menu was built,
    /// for the same reason the spelling suggestions read their own Text: these slots outlive
    /// every right-click.
    /// </summary>
    private void OnLinkSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string target } && _clickedLink is { } hit)
        {
            _ = ViewModel.RepointLinkAsync(hit, target);
        }
    }

    private void RemoveClickedLink()
    {
        if (_clickedLink is { } hit)
        {
            _ = ViewModel.RemoveLinkAsync(hit);
        }
    }

    /// <summary>
    /// Puts a chosen suggestion in place of the misspelling.
    ///
    /// The word is read from the item that was clicked rather than captured when the menu was
    /// built: these slots are reused for every right-click, so anything captured at build time
    /// would be a word from some earlier menu.
    /// </summary>
    private void OnSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Text.Length: > 0 } item && _clickedSpelling is { } hit)
        {
            _ = ViewModel.ReplaceSpellingAsync(hit, item.Text);
        }
    }

    /// <summary>
    /// Accepts the misspelled word permanently.
    ///
    /// The squiggles clear on the next pass without the engine being asked anything: the
    /// analyzer caches what the engine said rather than what survived the filter, so a new word
    /// only changes the filter.
    /// </summary>
    private void AddClickedWordToDictionary()
    {
        if (_clickedSpelling is { } hit)
        {
            _ = ViewModel.AddToDictionaryAsync(hit.Word);
        }
    }

    private void DeleteRepeatedWord()
    {
        if (_clickedSpelling is not { } hit)
        {
            return;
        }

        // The space in front goes with it, so "has has" becomes "has" rather than "has  ". There
        // is always something there: a word can only be flagged as a repeat if another one
        // precedes it on the same line.
        SpellingHit range = hit.Start > 0 ? hit with { Start = hit.Start - 1 } : hit;

        _ = ViewModel.ReplaceSpellingAsync(range, string.Empty);
    }

    /// <summary>
    /// The source pane's menu: the Edit menu, minus the entries that only make sense with
    /// the whole document in view. Every item routes to the same commands the header menu
    /// uses, so there is one implementation of Copy and one of Find.
    /// </summary>
    private MenuFlyout BuildSourceMenu()
    {
        if (_sourceMenu is not null)
        {
            return _sourceMenu;
        }

        var menu = new MenuFlyout();

        // Spelling goes first, above Undo, because a menu raised on a squiggle is a menu about
        // that word - which is where Word, Chrome and VS Code all put it. Every item here is
        // collapsed unless the pointer was actually on a misspelling.
        _suggestionItems = new MenuFlyoutItem[AppSettings.MaximumSpellSuggestionCount];

        for (int i = 0; i < _suggestionItems.Length; i++)
        {
            var suggestion = new MenuFlyoutItem { Visibility = Visibility.Collapsed };

            // The word is read from the item's own Text at click time rather than captured here:
            // these ten items outlive every right-click, and a closure over the loop variable
            // would be answering for whatever the slot held when the menu was built.
            suggestion.Click += OnSuggestionClick;

            _suggestionItems[i] = suggestion;
            menu.Items.Add(suggestion);
        }

        _noSuggestionsItem = new MenuFlyoutItem
        {
            Text = "No suggestions",
            IsEnabled = false,
            Visibility = Visibility.Collapsed,
        };

        _deleteRepeatedItem = new MenuFlyoutItem
        {
            Text = "Delete repeated word",
            Visibility = Visibility.Collapsed,
        };

        _deleteRepeatedItem.Click += (_, _) => DeleteRepeatedWord();

        _addToDictionaryItem = new MenuFlyoutItem
        {
            Text = "Add to Dictionary",
            Visibility = Visibility.Collapsed,
        };

        _addToDictionaryItem.Click += (_, _) => AddClickedWordToDictionary();

        _spellingSeparator = new MenuFlyoutSeparator { Visibility = Visibility.Collapsed };

        menu.Items.Add(_noSuggestionsItem);
        menu.Items.Add(_deleteRepeatedItem);
        menu.Items.Add(_addToDictionaryItem);
        menu.Items.Add(_spellingSeparator);

        // Dead links sit directly below the spelling block and above Undo, for the same reason
        // spelling is up there: a menu raised on a squiggle is a menu about that squiggle.
        //
        // Both blocks can be showing at once, and that is correct rather than a defect: a
        // misspelled word inside the label of a broken link - "[teh readme](gone.md)" - really is
        // two separate problems at one position, and hiding either would be answering a question
        // the user did not ask. Spelling comes first because it is about the word under the
        // pointer and the link is about the construct around it.
        _linkSuggestionItems = new MenuFlyoutItem[3];

        for (int i = 0; i < _linkSuggestionItems.Length; i++)
        {
            var suggestion = new MenuFlyoutItem { Visibility = Visibility.Collapsed };

            suggestion.Click += OnLinkSuggestionClick;

            _linkSuggestionItems[i] = suggestion;
            menu.Items.Add(suggestion);
        }

        _removeLinkItem = new MenuFlyoutItem
        {
            Text = "Remove this link",
            Visibility = Visibility.Collapsed,
        };

        _removeLinkItem.Click += (_, _) => RemoveClickedLink();

        _linkSeparator = new MenuFlyoutSeparator { Visibility = Visibility.Collapsed };

        menu.Items.Add(_removeLinkItem);
        menu.Items.Add(_linkSeparator);

        menu.Items.Add(Edit("Undo", "undo", "Ctrl+Z"));
        menu.Items.Add(Edit("Redo", "redo", "Ctrl+Y"));
        menu.Items.Add(new MenuFlyoutSeparator());

        _cutItem = Edit("Cut", "cut", "Ctrl+X");
        _sourceCopyItem = Edit("Copy", "copy", "Ctrl+C");

        menu.Items.Add(_cutItem);
        menu.Items.Add(_sourceCopyItem);
        menu.Items.Add(Edit("Paste", "paste", "Ctrl+V"));

        // Paste already takes an image when there is one, so this is the same command under a
        // name that says so. It exists because "Paste" alone gives no hint that a screenshot is
        // something the app will do anything sensible with, and it is shown only when there
        // really is one - an item that does nothing is worse than no item.
        _pasteImageItem = Edit("Paste Image", "paste", string.Empty);
        menu.Items.Add(_pasteImageItem);
        menu.Items.Add(NeedsContent(Edit("Select All", "selectAll", "Ctrl+A")));
        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(NeedsContent(Edit("Find...", "find", "Ctrl+F")));
        menu.Items.Add(NeedsContent(Edit("Replace...", "replace", "Ctrl+H")));
        menu.Items.Add(NeedsContent(Edit("Go to Line...", "gotoLine", "Ctrl+G")));
        menu.Items.Add(new MenuFlyoutSeparator());

        var format = new MenuFlyoutItem
        {
            Text = "Format Document",
            KeyboardAcceleratorTextOverride = "Shift+Alt+F",
        };

        format.Click += (_, _) => ViewModel.FormatDocumentCommand.Execute(null);
        menu.Items.Add(NeedsContent(format));

        _sourceMenu = menu;
        return menu;

        MenuFlyoutItem Edit(string text, string command, string accelerator)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                KeyboardAcceleratorTextOverride = accelerator,
            };

            // The accelerator text is a label, not a binding: the real accelerators are
            // registered once on the root, and declaring them again here would fire them
            // twice. Same reason the header menu gives for its own overrides.
            item.Click += (_, _) => ViewModel.EditActionCommand.Execute(command);

            return item;
        }
    }

    /// <summary>
    /// The preview's menu: reading and getting the document out, which is what the pane is
    /// for. Nothing that edits, because nothing in the preview is editable.
    /// </summary>
    private MenuFlyout BuildPreviewMenu()
    {
        if (_previewMenu is not null)
        {
            return _previewMenu;
        }

        var menu = new MenuFlyout();

        // Deliberately not the Edit menu's Copy: that one copies the editor's selection and
        // pulls the source pane into view to do it. The preview holds a selection of its own.
        _previewCopyItem = new MenuFlyoutItem { Text = "Copy", KeyboardAcceleratorTextOverride = "Ctrl+C" };
        _previewCopyItem.Click += (_, _) => ViewModel.CopyPreviewSelectionCommand.Execute(null);

        var selectAll = new MenuFlyoutItem { Text = "Select All", KeyboardAcceleratorTextOverride = "Ctrl+A" };
        selectAll.Click += (_, _) => ViewModel.SelectAllInPreviewCommand.Execute(null);

        menu.Items.Add(_previewCopyItem);
        menu.Items.Add(NeedsContent(selectAll));

        _targetSeparator = new MenuFlyoutSeparator();
        menu.Items.Add(_targetSeparator);

        _copyLinkItem = new MenuFlyoutItem { Text = "Copy Link" };
        _copyLinkItem.Click += (_, _) => CopyClicked(_clickedLinkUrl, "Link copied");

        _copyImageItem = new MenuFlyoutItem { Text = "Copy Image Address" };
        _copyImageItem.Click += (_, _) => CopyClicked(_clickedImageUrl, "Image address copied");

        menu.Items.Add(_copyLinkItem);
        menu.Items.Add(_copyImageItem);
        menu.Items.Add(new MenuFlyoutSeparator());

        // Copies whatever is selected in the preview, or the whole document when nothing
        // is, which is why it sits with the copy items rather than the exports.
        var richText = new MenuFlyoutItem
        {
            Text = "Copy as Rich Text",
            KeyboardAcceleratorTextOverride = "Ctrl+Shift+C",
        };
        richText.Click += (_, _) => ViewModel.CopyAsRichTextCommand.Execute(null);

        menu.Items.Add(NeedsContent(richText));
        menu.Items.Add(new MenuFlyoutSeparator());

        var pdf = new MenuFlyoutItem { Text = "Export to PDF..." };
        pdf.Click += (_, _) => ViewModel.ExportPdfCommand.Execute(null);

        var html = new MenuFlyoutItem { Text = "Export to HTML..." };
        html.Click += (_, _) => ViewModel.ExportHtmlCommand.Execute(null);

        var print = new MenuFlyoutItem { Text = "Print...", KeyboardAcceleratorTextOverride = "Ctrl+P" };
        print.Click += (_, _) => ViewModel.PrintCommand.Execute(null);

        menu.Items.Add(NeedsContent(pdf));
        menu.Items.Add(NeedsContent(html));
        menu.Items.Add(NeedsContent(print));

        _previewMenu = menu;
        return menu;
    }

    /// <summary>Registers an item as one that needs a document with something in it.</summary>
    private MenuFlyoutItem NeedsContent(MenuFlyoutItem item)
    {
        _contentItems.Add(item);
        return item;
    }

    private void CopyClicked(string? value, string announcement)
    {
        if (ClipboardText.Set(value, _logger))
        {
            ViewModel.StatusText = announcement;
        }
    }
}
