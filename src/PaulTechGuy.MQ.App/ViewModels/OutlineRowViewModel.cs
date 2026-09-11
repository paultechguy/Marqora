// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using PaulTechGuy.MQ.Domain;
using Windows.UI.Text;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// One heading in the outline panel.
///
/// A flat row rather than a node with children. The panel has no expanders - collapsing a
/// section was deliberately left out - so the tree is expressed by an indent, and an indent
/// is a number rather than a structure.
///
/// The indent and the weight are worked out once, here, rather than in a converter. Every
/// other binding in this app is an <c>x:Bind</c> to a function or a property, and a row is
/// created once and read many times, so there is nothing to be gained by computing them at
/// every repaint.
/// </summary>
public sealed class OutlineRowViewModel
{
    /// <summary>
    /// How far each heading level is pushed in.
    ///
    /// Small enough that six levels do not consume a panel that opens at 240, which is what
    /// a more generous step does: at 16 the deepest heading starts halfway across.
    /// </summary>
    private const double IndentPerLevel = 13;

    /// <summary>
    /// The gap between a row's number and its words: two non-breaking spaces, which is what
    /// the preview puts there, so the panel and the page are spaced alike.
    ///
    /// Written as a character rather than as ordinary spaces in a literal for two reasons:
    /// XAML's text layout collapses a run of spaces to one, and a gap that cannot be seen in
    /// the source is a gap the next person deletes by accident.
    /// </summary>
    private static readonly string NumberGap = new((char)0x00A0, 2);

    public OutlineRowViewModel(OutlineHeading heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        Level = heading.Level;
        Text = heading.Text;
        Number = heading.Number;
        SourceLine = heading.SourceLine;

        NumberLabel = heading.Number.Length == 0 ? string.Empty : heading.Number + NumberGap;

        // Levels are 1-based, so an H1 sits flush against the panel's own padding.
        Indent = new Thickness((heading.Level - 1) * IndentPerLevel, 0, 0, 0);

        // Top-level headings carry the document's shape and are what the eye should find
        // first when the panel is scanned rather than read.
        Weight = heading.Level == 1 ? FontWeights.SemiBold : FontWeights.Normal;
    }

    public int Level { get; }

    public string Text { get; }

    /// <summary>
    /// The section number, as "1.2.1", or empty when the document is not numbered or the
    /// heading sits above the level the count starts at.
    ///
    /// Apart from <see cref="Text"/> because the two are shown differently and searched
    /// differently: the number is dimmed, and the filter box matches only the words.
    /// </summary>
    public string Number { get; }

    /// <summary>The number as the row draws it, with the gap that holds it off the words.</summary>
    public string NumberLabel { get; }

    /// <summary>
    /// What Copy puts on the clipboard: the row as it reads on screen.
    ///
    /// With an ordinary space rather than the pair the row is drawn with. Somewhere else is
    /// about to receive this, and a non-breaking space pasted into a document is a character
    /// that looks like a space until something wraps.
    /// </summary>
    public string CopyText => Number.Length == 0 ? Text : $"{Number} {Text}";

    /// <summary>Zero-based line in the markdown source, which is what the jump uses.</summary>
    public int SourceLine { get; }

    public Thickness Indent { get; }

    public FontWeight Weight { get; }

    /// <summary>
    /// What the row says on hover: the level and the line, which the row itself has no room
    /// for and which are the two things that disambiguate two headings with the same words.
    /// </summary>
    public string Tooltip => $"H{Level}  ·  line {SourceLine + 1}";

    /// <summary>
    /// Falls back to the heading's own text if a row is ever shown without a template.
    /// A plain ToString here is a dump of the type name, which is what the results list in
    /// Find All learned to override for the same reason.
    /// </summary>
    public override string ToString() => Text;
}
