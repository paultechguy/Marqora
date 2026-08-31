// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>Replaces everything in <see cref="Range"/> with <see cref="Text"/>.</summary>
public sealed record TextEdit(TextRange Range, string Text);

/// <summary>
/// What an editing command decided to do.
///
/// Edits are expressed against the document as it was when the command ran, so they must
/// be applied as one batch rather than one after another. <see cref="Selection"/> is where
/// the caret should end up afterwards; null leaves it wherever the edits push it.
/// </summary>
public sealed record EditResult(IReadOnlyList<TextEdit> Edits, TextRange? Selection)
{
    /// <summary>A command that decided there was nothing to do.</summary>
    public static EditResult None { get; } = new([], null);

    public bool IsEmpty => Edits.Count == 0;
}
