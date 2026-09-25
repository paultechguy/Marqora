// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.Domain;
using Windows.UI;
using Windows.UI.Text;

namespace PaulTechGuy.MQ.App.Controls;

/// <summary>
/// A review comment as the sidebar's card shows it: the five styles of <see cref="CommentMarkup"/>
/// drawn, rather than the asterisks and equals signs that write them.
///
/// Bold, italic and underline are properties of a run. Code and highlight also need a color behind
/// the text, which a run cannot carry, so each is drawn as a small box of its own in the line (see
/// Box). A RichTextBlock's text highlighters were tried for that first; they are placed by
/// character index, and WinUI's count of an inline box and of the runs' boundaries slid the yellow
/// several letters along whenever code came earlier on the line. A box has no position to get
/// wrong. The comment is a stack of RichTextBlocks, one per line; a blank line between paragraphs
/// becomes a small gap.
///
/// The colors are plain colors, not theme brushes looked up in code, which resolve against the
/// operating system's theme rather than the one chosen in Marqora (see PaletteWindow.SurfaceBrush).
/// The highlight is the preview's own yellow, <see cref="CalloutColors.MarkHex"/>, with black text
/// on it in either theme, as the preview's highlight has; code wears a gray that is quiet on both
/// the light card and the dark one.
/// </summary>
public sealed partial class CommentText : UserControl
{
    public static readonly DependencyProperty MarkupProperty = DependencyProperty.Register(
        nameof(Markup),
        typeof(string),
        typeof(CommentText),
        new PropertyMetadata(string.Empty, (d, _) => ((CommentText)d).Rebuild()));

    private static readonly Color HighlightColor = HexColor.Parse(CalloutColors.MarkHex, nameof(CalloutColors));

    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas");

    private static readonly Color Black = Color.FromArgb(0xFF, 0, 0, 0);

    // Gray at a strength that reads on the light card and the dark one alike, so no theme lookup is needed.
    private static readonly Color CodeFill = Color.FromArgb(0x30, 0x80, 0x80, 0x80);

    private static readonly Color CodeBorder = Color.FromArgb(0x55, 0x80, 0x80, 0x80);

    private readonly StackPanel _lines = new() { Spacing = 0 };

    public CommentText()
    {
        Content = _lines;
        IsTabStop = false;
    }

    /// <summary>The comment as written, markup and all.</summary>
    public string Markup
    {
        get => (string)GetValue(MarkupProperty);
        set => SetValue(MarkupProperty, value);
    }

    private void Rebuild()
    {
        _lines.Children.Clear();

        string text = (Markup ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        foreach (string line in text.Split('\n'))
        {
            if (line.Trim().Length == 0)
            {
                _lines.Children.Add(new Border { Height = 6 });
                continue;
            }

            _lines.Children.Add(BuildLine(line));
        }
    }

    private static RichTextBlock BuildLine(string line)
    {
        var block = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var paragraph = new Paragraph();

        foreach (CommentSpan span in CommentMarkup.Parse(line))
        {
            // Code and highlight each need a color behind the text, and both are drawn as a small
            // box in the line. A TextHighlighter was tried for the highlight first; it is placed
            // by character index, and WinUI counts a code box and the runs' own boundaries in ways
            // that slid the yellow several letters along whenever code came earlier on the line.
            // A box has no position to get wrong.
            if (span.Style.HasFlag(CommentStyle.Code) || span.Style.HasFlag(CommentStyle.Highlight))
            {
                paragraph.Inlines.Add(Box(span));
                continue;
            }

            var run = new Run { Text = span.Text };
            ApplyStyle(run, span.Style);
            paragraph.Inlines.Add(run);
        }

        block.Blocks.Add(paragraph);

        return block;
    }

    private static void ApplyStyle(Run run, CommentStyle style)
    {
        if (style.HasFlag(CommentStyle.Bold))
        {
            run.FontWeight = FontWeights.SemiBold;
        }

        if (style.HasFlag(CommentStyle.Italic))
        {
            run.FontStyle = FontStyle.Italic;
        }

        if (style.HasFlag(CommentStyle.Underline))
        {
            run.TextDecorations = TextDecorations.Underline;
        }
    }

    /// <summary>
    /// Inline code, or highlighted words, as the preview draws them: a small box of their own in
    /// the line. Code has rounded corners and a hairline border, as the preview's inline code does;
    /// a highlight is the preview's yellow with black text, as its ==highlight== is, and code that
    /// is highlighted as well is a yellow code box. The box sits on the line's baseline by its
    /// bottom edge, which would lift it above the words beside it, so it is moved down by about a
    /// descent's worth. It does not break across lines - a code span in a comment is a name or a
    /// short command, and one that is not wraps inside its own box instead.
    /// </summary>
    private static InlineUIContainer Box(CommentSpan span)
    {
        bool highlighted = span.Style.HasFlag(CommentStyle.Highlight);
        bool code = span.Style.HasFlag(CommentStyle.Code);

        var text = new TextBlock
        {
            Text = span.Text,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = false,
        };

        if (code)
        {
            text.FontFamily = CodeFont;
            text.FontSize = 12.5;
        }

        if (span.Style.HasFlag(CommentStyle.Bold))
        {
            text.FontWeight = FontWeights.SemiBold;
        }

        if (span.Style.HasFlag(CommentStyle.Italic))
        {
            text.FontStyle = FontStyle.Italic;
        }

        if (span.Style.HasFlag(CommentStyle.Underline))
        {
            text.TextDecorations = TextDecorations.Underline;
        }

        if (highlighted)
        {
            text.Foreground = new SolidColorBrush(Black);
        }

        return new InlineUIContainer
        {
            Child = new Border
            {
                Child = text,
                CornerRadius = new CornerRadius(code ? 4 : 3),
                BorderThickness = new Thickness(code ? 1 : 0),
                BorderBrush = new SolidColorBrush(CodeBorder),
                Background = new SolidColorBrush(highlighted ? HighlightColor : CodeFill),
                Padding = code ? new Thickness(4, 0, 4, 1) : new Thickness(2, 0, 2, 1),
                Margin = new Thickness(1, 0, 1, 0),
                MaxWidth = 280,
                RenderTransform = new TranslateTransform { Y = 4 },
            },
        };
    }
}
