// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Applies the requested theme to the window and reports what it actually resolved to.
///
/// Under <see cref="AppTheme.System"/> the element theme is left at Default and Windows
/// decides. ActualThemeChanged then reports the resolution, including later changes made in
/// Windows Settings while the app is running, which is what the WebView needs to follow.
/// </summary>
public sealed class ThemeService(WindowContext window, ILogger<ThemeService> logger) : IThemeService
{
    private FrameworkElement? _root;

    public AppTheme Requested { get; private set; } = AppTheme.System;

    public AppTheme Effective { get; private set; } = AppTheme.Dark;

    public event EventHandler<AppTheme>? EffectiveThemeChanged;

    public void Apply(AppTheme theme)
    {
        Requested = theme;

        if (!TryAttachRoot())
        {
            // Applied again once the window has content.
            return;
        }

        _root!.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        UpdateEffective();
        logger.LogInformation("Theme set to {Requested}, resolved to {Effective}.", Requested, Effective);
    }

    private bool TryAttachRoot()
    {
        if (_root is not null)
        {
            return true;
        }

        if (window.Window?.Content is not FrameworkElement root)
        {
            return false;
        }

        _root = root;
        _root.ActualThemeChanged += OnActualThemeChanged;

        return true;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args) => UpdateEffective();

    private void UpdateEffective()
    {
        if (_root is null)
        {
            return;
        }

        AppTheme resolved = _root.ActualTheme == ElementTheme.Dark ? AppTheme.Dark : AppTheme.Light;

        if (resolved == Effective)
        {
            return;
        }

        Effective = resolved;
        EffectiveThemeChanged?.Invoke(this, resolved);
    }
}
