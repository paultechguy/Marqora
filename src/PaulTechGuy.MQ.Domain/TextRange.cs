// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// A caret location in a document. Both values are zero-based, matching Markdig and the
/// editor's own model; Monaco's one-based line and column numbers are converted at the
/// bridge rather than here, so everything inside the app counts from zero.
/// </summary>
public readonly record struct TextPosition(int Line, int Column)
{
    public static TextPosition Origin => new(0, 0);
}

/// <summary>
/// A span between two positions. <see cref="Start"/> is not required to precede
/// <see cref="End"/>: a selection dragged upwards arrives reversed, and commands care
/// about the covered text rather than the direction it was selected in.
/// </summary>
public readonly record struct TextRange(TextPosition Start, TextPosition End)
{
    public static TextRange At(TextPosition position) => new(position, position);

    public bool IsEmpty => Start == End;

    /// <summary>True when the span begins and ends on the same line.</summary>
    public bool IsSingleLine => Start.Line == End.Line;

    /// <summary>The same span with <see cref="Start"/> guaranteed to come first.</summary>
    public TextRange Ordered =>
        Start.Line < End.Line || (Start.Line == End.Line && Start.Column <= End.Column)
            ? this
            : new TextRange(End, Start);
}
