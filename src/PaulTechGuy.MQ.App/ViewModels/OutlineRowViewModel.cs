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

    public OutlineRowViewModel(OutlineHeading heading)
    {
        ArgumentNullException.ThrowIfNull(heading);

        Level = heading.Level;
        Text = heading.Text;
        SourceLine = heading.SourceLine;

        // Levels are 1-based, so an H1 sits flush against the panel's own padding.
        Indent = new Thickness((heading.Level - 1) * IndentPerLevel, 0, 0, 0);

        // Top-level headings carry the document's shape and are what the eye should find
        // first when the panel is scanned rather than read.
        Weight = heading.Level == 1 ? FontWeights.SemiBold : FontWeights.Normal;
    }

    public int Level { get; }

    public string Text { get; }

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
