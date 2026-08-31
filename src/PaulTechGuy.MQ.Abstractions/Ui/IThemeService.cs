// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// Applies the chosen theme to the window and reports the effective theme once
/// System has been resolved against the OS setting.
/// </summary>
public interface IThemeService
{
    AppTheme Requested { get; }

    /// <summary>Always Light or Dark; never System.</summary>
    AppTheme Effective { get; }

    /// <summary>Raised when the effective theme changes, including from an OS-level change.</summary>
    event EventHandler<AppTheme>? EffectiveThemeChanged;

    void Apply(AppTheme theme);
}
