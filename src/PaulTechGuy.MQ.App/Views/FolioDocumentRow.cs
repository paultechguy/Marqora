// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// One document in the Folio preflight's list: whether it travels, and where it sits in the
/// order.
///
/// Public, and with ordinary properties raising <see cref="PropertyChanged"/>, because the row
/// template reaches them through a classic <c>{Binding}</c> - reflection, which does not see an
/// internal type - and because Select all has to move the tick marks it did not draw.
///
/// The name and the folder are held apart rather than joined into one label. A row has a fixed
/// width and a file name has none, so the two have to trim differently: the name is what the
/// reader is looking for and is never cut, while the folder is context and gives up its tail
/// first. Joining them would make the name compete with the path for the same characters.
/// </summary>
public sealed class FolioDocumentRow : INotifyPropertyChanged
{
    private bool _isIncluded = true;
    private string _warningGlyph = string.Empty;

    public required string Name { get; init; }

    /// <summary>
    /// The folder the document is in, or empty when every document in the Folio shares one -
    /// in which case repeating it on twelve rows says nothing and costs the width the names want.
    /// </summary>
    public required string Folder { get; init; }

    public required string FullPath { get; init; }

    /// <summary>Whether this document travels. The tick in the row, not the row's selection.</summary>
    public bool IsIncluded
    {
        get => _isIncluded;
        set => Set(ref _isIncluded, value);
    }

    /// <summary>
    /// A warning sign when the preflight found something wrong in this document, and an empty
    /// string otherwise.
    ///
    /// A glyph rather than a visibility, so the template needs no converter and no theme lookup -
    /// the trap a code-built surface falls into, because a brush resolved in code answers to the
    /// application's theme rather than the one the user chose.
    /// </summary>
    public string WarningGlyph
    {
        get => _warningGlyph;
        set => Set(ref _warningGlyph, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The fallback if the row template ever fails to parse: a readable line.</summary>
    public override string ToString() => Name;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
