// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// What an export could not carry across, gathered as it goes.
///
/// A Word export never refuses - a picture that is not on this machine, a diagram that would
/// not draw, an equation using something Word has no form of, each cost the document that one
/// thing and nothing else. The trade is that the reader has to be told, or the document has a
/// hole in it that only the person it was sent to will find.
///
/// Deduplicating is the point of having a type rather than a list. A document with twenty
/// equations that all use the same unmapped construct has one thing wrong with it, not twenty,
/// and a dialog listing the same sentence twenty times says less than one saying it once.
/// </summary>
internal sealed class ExportReport
{
    private readonly List<string> _messages = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Messages => _messages;

    /// <summary>Records something the document did not get, unless it has been said already.</summary>
    public void Note(string message)
    {
        if (!string.IsNullOrWhiteSpace(message) && _seen.Add(message))
        {
            _messages.Add(message);
        }
    }

    /// <summary>
    /// An equation written as its TeX source because the converter met something it does not
    /// map.
    ///
    /// The element is named on purpose. Nobody can enumerate every shape TeX can produce, so
    /// the way this converter gets better is by finding out which constructs real documents
    /// actually use - and that only happens if each miss says what it was rather than quietly
    /// falling back.
    /// </summary>
    public void UnsupportedMath(string? element) =>
        Note(element is { Length: > 0 }
            ? $"An equation used <{element}>, which has no Word form - its source is in the document instead"
            : "An equation could not be converted - its source is in the document instead");
}
