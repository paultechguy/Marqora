// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>One row of the results list.</summary>
internal abstract record FindRow(Guid DocumentId);

/// <summary>The heading a document's matches sit under.</summary>
internal sealed record FindHeadingRow(Guid DocumentId, string Name, string Path, int Count) : FindRow(DocumentId)
{
    /// <summary>
    /// What a row falls back to if it is ever shown without being built. A record's own
    /// ToString is a dump of its properties, which is what a whole list of these looked like
    /// the one time the row builder did not run.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>One match, under the heading for the document it was found in.</summary>
internal sealed record FindMatchRow(Guid DocumentId, FindMatch Match) : FindRow(DocumentId)
{
    /// <inheritdoc cref="FindHeadingRow.ToString"/>
    public override string ToString() => $"{Match.Line + 1}  {Match.LineText.Trim()}";
}

/// <summary>
/// The results list.
///
/// Documents and their matches are flattened into one list of rows rather than grouped
/// through a CollectionViewSource, because grouping wants DataTemplates and the interesting
/// part of a row — the matched run, tinted inside the line around it — cannot come from a
/// template anyway: TextBlock.Inlines and TextHighlighters are built in code or not at all.
///
/// Rows are built in <see cref="PrepareContainerForItemOverride"/>, so the list still
/// virtualizes. That matters: a loose search across a dozen open documents can produce
/// thousands of rows, and realising them all would freeze the window it was meant to fill.
/// </summary>
internal sealed partial class FindResultsList : ListView
{
    /// <summary>Room for four digits, which is more lines than most markdown files have.</summary>
    private const int LineNumberWidth = 44;

    /// <summary>Where a line stops being worth showing. Long enough for a full sentence.</summary>
    private const int MaxLineLength = 240;

    /// <summary>How much of a long line to keep in front of the match, for context.</summary>
    private const int LeadingContext = 40;

    public FindResultsList()
    {
        SelectionMode = ListViewSelectionMode.Single;
        IsItemClickEnabled = false;
        ContainerContentChanging += OnContainerContentChanging;
    }

    /// <summary>How far a closed document's rows fade. Still readable, plainly past tense.</summary>
    private const double ClosedOpacity = 0.45;

    /// <summary>
    /// The tint drawn behind a match. Supplied by the window, which knows the effective
    /// theme; a brush looked up here would resolve against the application's theme instead,
    /// which is not the same thing once the user has chosen one.
    /// </summary>
    public Brush? Highlight { get; set; }

    /// <summary>
    /// The text drawn on that tint, or null to leave the row's own colour alone. Supplied by
    /// the window for the same reason <see cref="Highlight"/> is.
    /// </summary>
    public Brush? HighlightForeground { get; set; }

    /// <summary>
    /// Documents that have been closed since the search ran. Held by the window and mutated
    /// in place, so a rebuild is all it takes to show one greying out.
    /// </summary>
    public IReadOnlySet<Guid> ClosedDocuments { get; set; } = new HashSet<Guid>();

    /// <summary>
    /// Fills each row as it is realised, and again as containers are recycled past it.
    ///
    /// This is an event rather than an override of PrepareContainerForItemOverride, and the
    /// difference is not cosmetic: that protected virtual is not routed back into a managed
    /// subclass of a WinUI control, so the override compiled, never ran, and every row drew
    /// itself as the record's own ToString - a wall of "FindMatchRow { DocumentId = ... }".
    /// ContainerContentChanging is the documented hook for code-built content in a
    /// virtualised list and is a plain event, with no composition in the way.
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
        container.Padding = new Thickness(10, 1, 10, 1);
        container.MinHeight = 0;

        // Otherwise the framework's own template puts the data item back as the content.
        container.ContentTemplate = null;

        if (args.Item is not FindRow row)
        {
            container.Content = null;
            return;
        }

        bool closed = ClosedDocuments.Contains(row.DocumentId);

        container.Opacity = closed ? ClosedOpacity : 1;

        switch (row)
        {
            case FindHeadingRow heading:
                container.Content = BuildHeading(heading, closed);
                ToolTipService.SetToolTip(container, heading.Path);
                break;

            case FindMatchRow match:
                container.Content = BuildMatch(match);
                ToolTipService.SetToolTip(container, match.Match.LineText);
                break;

            default:
                container.Content = null;
                break;
        }

        args.Handled = true;
    }

    private static Grid BuildHeading(FindHeadingRow heading, bool closed)
    {
        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = closed ? $"{heading.Name} (closed)" : heading.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var count = new TextBlock
        {
            Text = heading.Count == 1 ? "1 match" : $"{heading.Count} matches",
            FontSize = 12,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(name, 0);
        Grid.SetColumn(count, 1);
        grid.Children.Add(name);
        grid.Children.Add(count);

        return grid;
    }

    private Grid BuildMatch(FindMatchRow row)
    {
        (string text, int start) = Excerpt(row.Match);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LineNumberWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // One-based here and nowhere else inside the app: this is the number in the gutter,
        // and the number someone would say out loud.
        var number = new TextBlock
        {
            Text = $"{row.Match.Line + 1}",
            FontSize = 12,
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var line = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // The excerpt can end mid-match on a very long line, so the tint is clamped to what
        // is actually on the row rather than to what was matched.
        int length = Math.Min(row.Match.Length, Math.Max(0, text.Length - start));

        if (length > 0 && Highlight is not null)
        {
            var highlighter = new TextHighlighter { Background = Highlight };

            if (HighlightForeground is not null)
            {
                highlighter.Foreground = HighlightForeground;
            }

            highlighter.Ranges.Add(new Microsoft.UI.Xaml.Documents.TextRange
            {
                StartIndex = start,
                Length = length,
            });

            line.TextHighlighters.Add(highlighter);
        }

        Grid.SetColumn(number, 0);
        Grid.SetColumn(line, 1);
        grid.Children.Add(number);
        grid.Children.Add(line);

        return grid;
    }

    /// <summary>
    /// The part of the line worth putting on a row, and where the match sits within it.
    ///
    /// Indentation goes first: a match three levels down a list would otherwise start halfway
    /// across the row with nothing but spaces in front of it. A line longer than the row can
    /// show is then cut to a window around the match, so the match is always visible rather
    /// than trimmed away by the ellipsis at the end.
    /// </summary>
    private static (string Text, int Start) Excerpt(FindMatch match)
    {
        string line = match.LineText;
        int start = match.Column;

        int indent = 0;

        while (indent < start && char.IsWhiteSpace(line[indent]))
        {
            indent++;
        }

        line = line[indent..];
        start -= indent;

        if (line.Length <= MaxLineLength)
        {
            return (line, start);
        }

        if (start <= LeadingContext)
        {
            return (line[..MaxLineLength] + "…", start);
        }

        int from = start - LeadingContext;
        int to = Math.Min(line.Length, from + MaxLineLength);

        // The ellipsis stands in for what was cut, and takes a column of its own.
        return ("…" + line[from..to], (start - from) + 1);
    }
}
