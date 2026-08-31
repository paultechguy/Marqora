// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace PaulTechGuy.MQ.App.Controls;

/// <summary>
/// A <see cref="Button"/> that shows the hand cursor over its hit area.
///
/// Deriving is the whole reason this type exists: WinUI exposes the cursor as the protected
/// <c>UIElement.ProtectedCursor</c>, so an element can only be given one from the inside.
/// It adds nothing else and wears whatever style is applied to it.
/// </summary>
public sealed class HandCursorButton : Button
{
    public HandCursorButton() =>
        this.ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
}
