// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using Windows.Graphics;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// The chrome every secondary window in Marqora wears: a resizable palette that floats above
/// the editor, stays out of Alt+Tab and the taskbar, paints its caption to match the theme, and
/// remembers where it was left.
///
/// This exists because three windows were about to carry the same code. The cheatsheet and Find
/// All each had their own copy of all of it - including the caption color table, written out
/// twice with the same hex values - and a preferences window would have made a third. What
/// differs between them is a name for the log, a minimum size, and which settings property the
/// placement lands in; everything else was identical, down to the whitespace.
///
/// Subclasses keep what is genuinely theirs: their content, when they show and hide, and what
/// they do about a theme change beyond the caption. This only owns the window itself.
/// </summary>
public abstract class PaletteWindow : Window
{
    private readonly string _name;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IntPtr _ownerHandle;
    private readonly ILogger _logger;

    private bool _isOwned;
    private bool _hasRestoredPlacement;

    /// <param name="name">
    /// What this window is called in the log. Only ever read by logging, so it is prose rather
    /// than an identifier: "Cheatsheet", "Find All".
    /// </param>
    /// <param name="ownerHandle">
    /// The main window, or zero. See <see cref="EnsureOwned"/> for why it is a handle rather
    /// than a Window, and why it cannot be applied in the constructor.
    /// </param>
    protected PaletteWindow(
        string name,
        int minimumWidth,
        int minimumHeight,
        ISettingsService settings,
        IThemeService theme,
        IntPtr ownerHandle,
        ILogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(logger);

        _name = name;
        _settings = settings;
        _theme = theme;
        _ownerHandle = ownerHandle;
        _logger = logger;

        MinimumWidth = minimumWidth;
        MinimumHeight = minimumHeight;
    }

    /// <summary>
    /// The narrowest and shortest the window may be dragged - and, for one that cannot be
    /// dragged at all, simply the size it is. See <see cref="IsResizable"/>.
    /// </summary>
    protected int MinimumWidth { get; }

    protected int MinimumHeight { get; }

    /// <summary>
    /// Whether the user may resize this window.
    ///
    /// True for a palette, which is read alongside the document and wants to be whatever size
    /// suits the reader. A subclass that is really a dialog overrides it: a settings form has a
    /// fixed sidebar and fixed-width fields, so extra width becomes dead space rather than more
    /// room, and a size nobody can change is one less thing to remember and restore.
    ///
    /// When this is false, <see cref="MinimumWidth"/> and <see cref="MinimumHeight"/> stop being
    /// a floor and become the size.
    /// </summary>
    protected virtual bool IsResizable => true;

    /// <summary>The Win32 handle, which is what window ownership is expressed in.</summary>
    public IntPtr Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>
    /// Where this window was last left, or a default. Never null: read it through the settings
    /// record's own accessor, which supplies a default rather than returning nothing.
    /// </summary>
    protected abstract WindowPlacement SavedPlacement { get; }

    /// <summary>
    /// Puts <paramref name="placement"/> into the settings record under this window's own key.
    ///
    /// A method rather than a settable property because the settings are an immutable record:
    /// the only way to change one is to produce another, and only the subclass knows which
    /// member of it belongs to this window.
    /// </summary>
    protected abstract AppSettings StorePlacement(AppSettings settings, WindowPlacement placement);

    // ------------------------------------------------------------------- chrome

    /// <summary>
    /// A reference palette rather than a second document: resizable, but with nothing to
    /// maximize or minimize, and out of Alt+Tab and the taskbar. The menu is how each of these
    /// is recalled, so a taskbar button would only be a second, inconsistent way to manage it.
    ///
    /// The flags are set individually rather than through CreateForToolWindow, whose defaults
    /// do not survive being applied to a XAML Window's AppWindow.
    /// </summary>
    protected void ConfigurePresenter()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = IsResizable;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.PreferredMinimumWidth = MinimumWidth;
            presenter.PreferredMinimumHeight = MinimumHeight;
        }

        AppWindow.IsShownInSwitchers = false;

        if (AppImages.HasIcon)
        {
            try
            {
                AppWindow.SetIcon(AppImages.IconPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not apply the window icon for {Window}.", _name);
            }
        }

        ApplyTitleBarTheme(_theme.Effective);
    }

    /// <summary>
    /// Paints the caption to match the page below it.
    ///
    /// Left alone, Windows draws the caption in the user's accent color, which on a window
    /// that is almost entirely one document reads as a stripe of unrelated color. The main
    /// window sidesteps this by extending its content into the title bar; these are too small
    /// to give up the caption, so the caption is colored instead.
    /// </summary>
    protected void ApplyTitleBarTheme(AppTheme theme)
    {
        AppWindowTitleBar bar = AppWindow.TitleBar;

        bool dark = theme == AppTheme.Dark;

        /*
            Say which mode the caption is in, before saying what to paint it.

            This is the line that actually decides. PreferredTheme defaults to
            UseDefaultAppMode, which follows the *system* app mode rather than the app's - so on
            a machine with Windows in dark mode and Marqora set to light, the caption came up
            dark no matter what the colors below said. The colors were being applied inside a
            dark title bar rather than replacing it.

            The colors still earn their place: they match the caption to the page beneath it
            rather than to the stock light or dark grey, which is the whole reason this method
            exists. PreferredTheme decides the mode; the rest decides the shade.
        */
        bar.PreferredTheme = dark ? TitleBarTheme.Dark : TitleBarTheme.Light;

        Windows.UI.Color surface = dark ? Rgb(0x27, 0x27, 0x27) : Rgb(0xF6, 0xF6, 0xF6);
        Windows.UI.Color text = dark ? Rgb(0xE6, 0xE6, 0xE6) : Rgb(0x1B, 0x1B, 0x1B);
        Windows.UI.Color muted = Rgb(0x8A, 0x8A, 0x8A);
        Windows.UI.Color hover = dark ? Rgb(0x38, 0x38, 0x38) : Rgb(0xE6, 0xE6, 0xE6);
        Windows.UI.Color pressed = dark ? Rgb(0x4A, 0x4A, 0x4A) : Rgb(0xD0, 0xD0, 0xD0);

        bar.BackgroundColor = surface;
        bar.InactiveBackgroundColor = surface;
        bar.ForegroundColor = text;
        bar.InactiveForegroundColor = muted;

        bar.ButtonBackgroundColor = surface;
        bar.ButtonInactiveBackgroundColor = surface;
        bar.ButtonForegroundColor = text;
        bar.ButtonInactiveForegroundColor = muted;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = text;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = text;
    }

    /// <summary>
    /// Paints the caption for whatever theme is in force now.
    ///
    /// Worth calling again after the window is shown. Caption colors set before a window has
    /// ever been displayed do not always survive it being brought up - Window.Activate in
    /// particular re-initialises the title bar and drops them, which showed up as a dark caption
    /// over light content. It also covers the theme having changed while the window was closed.
    /// </summary>
    protected void RefreshTitleBar() => ApplyTitleBarTheme(_theme.Effective);

    /// <summary>An opaque color from three bytes. Shared: subclasses paint their own surfaces too.</summary>
    protected static Windows.UI.Color Rgb(byte r, byte g, byte b) =>
        Windows.UI.Color.FromArgb(0xFF, r, g, b);

    /// <summary>
    /// The page behind a palette's content.
    ///
    /// WinUI's own two values, written out rather than looked up, and this is a trap worth
    /// knowing about. A code lookup of ApplicationPageBackgroundThemeBrush resolves against the
    /// *application's* theme, which is the operating system's - but these windows follow the
    /// theme the user chose in Marqora. With Windows dark and Marqora set to light, the lookup
    /// paints a black page under light controls.
    ///
    /// It is invisible in the cheatsheet and the diagram window, which both do that lookup,
    /// because a WebView covers every pixel of them. It is extremely visible anywhere else, so
    /// any palette that shows its own background should use this.
    /// </summary>
    protected static SolidColorBrush SurfaceBrush(AppTheme theme) =>
        new(theme == AppTheme.Dark ? Rgb(0x20, 0x20, 0x20) : Rgb(0xF3, 0xF3, 0xF3));

    /// <summary>
    /// Makes the main window this window's owner, once, the first time it is shown.
    ///
    /// An owned window always floats above its owner, so a palette cannot end up buried behind
    /// the editor. That is worth more than it first appears: it is what lets a menu item be a
    /// plain toggle. Without ownership, opening the menu activates the main window and raises it
    /// over the palette, so any rule asking "can the user see it right now" would be answering
    /// about a state the click itself had just changed.
    ///
    /// Ownership is not modality - the main window stays fully usable. It also means the palette
    /// minimises and restores along with the editor, which is what one expects of something
    /// belonging to a document window.
    ///
    /// It has to happen after the first show, not in the constructor. Setting the owner on a
    /// window WinUI has created but not yet displayed leaves it without WS_VISIBLE, and every
    /// later AppWindow.Show() silently does nothing.
    /// </summary>
    protected void EnsureOwned()
    {
        if (_isOwned)
        {
            return;
        }

        _isOwned = true;

        if (_ownerHandle == IntPtr.Zero)
        {
            _logger.LogWarning("{Window} has no owner window; it may fall behind the editor.", _name);

            return;
        }

        _ = SetWindowLongPtr(Handle, GwlpHwndParent, _ownerHandle);

        _logger.LogDebug("{Window} is now owned by the main window.", _name);
    }

    // ---------------------------------------------------------------- placement

    /// <summary>
    /// Remembers where the window is, unless it is hidden or absurdly small - either of which
    /// would record a position nobody could get back from.
    /// </summary>
    protected void CapturePlacement()
    {
        AppWindow window = AppWindow;

        if (!window.IsVisible
            || window.Size.Width < MinimumWidth
            || window.Size.Height < MinimumHeight)
        {
            return;
        }

        var placement = new WindowPlacement
        {
            X = window.Position.X,
            Y = window.Position.Y,
            Width = window.Size.Width,
            Height = window.Size.Height,
        };

        _settings.Update(s => StorePlacement(s, placement));
    }

    /// <summary>
    /// Remembers the geometry as the window is moved or resized, rather than on the way out.
    ///
    /// That timing is the whole point. A capture that waited for the close missed every exit
    /// except the caption's X: Window.Close - which is what an OK or a Cancel button calls -
    /// does not go through AppWindow.Closing, so the position was never written and the window
    /// opened in the same default spot every time.
    ///
    /// For subclasses with no AppWindow.Changed handler of their own. The cheatsheet and Find
    /// All do the same thing inside theirs, alongside work specific to them.
    /// </summary>
    protected void TrackPlacementChanges() =>
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange)
            {
                CapturePlacement();
            }
        };

    /// <summary>
    /// Puts the window back where it was, once per lifetime.
    /// </summary>
    /// <param name="nearby">
    /// The main window's rectangle, used only when there is nothing remembered: a first opening
    /// sits just inside its right edge, which reads as belonging to it without covering the
    /// document.
    /// </param>
    protected void RestorePlacement(RectInt32 nearby)
    {
        if (_hasRestoredPlacement)
        {
            return;
        }

        _hasRestoredPlacement = true;

        WindowPlacement placement = SavedPlacement;

        // A window nobody can resize opens at its own size every time, whatever an older
        // settings file happens to remember. Only where it sits is the user's to choose.
        int width = IsResizable ? Math.Max(placement.Width, MinimumWidth) : MinimumWidth;
        int height = IsResizable ? Math.Max(placement.Height, MinimumHeight) : MinimumHeight;

        if (placement.HasPosition && FitsOnADisplay(placement.X, placement.Y, width, height))
        {
            AppWindow.MoveAndResize(new RectInt32(placement.X, placement.Y, width, height));

            return;
        }

        RectInt32 fallback = DefaultPosition(nearby, width, height);

        AppWindow.MoveAndResize(
            FitsOnADisplay(fallback.X, fallback.Y, fallback.Width, fallback.Height)
                ? fallback
                : new RectInt32(nearby.X + 48, nearby.Y + 48, width, height));
    }

    /// <summary>
    /// Where to sit the first time, before anything has been remembered.
    ///
    /// Just inside the main window's right edge, which reads as belonging to it without covering
    /// the document - the right answer for something you glance at while typing. A subclass that
    /// is really a dialog rather than a palette overrides this to centre instead.
    /// </summary>
    protected virtual RectInt32 DefaultPosition(RectInt32 nearby, int width, int height) =>
        new(nearby.X + Math.Max(0, nearby.Width - width - 48), nearby.Y + 64, width, height);

    /// <summary>Centred on <paramref name="nearby"/>, for the subclasses that want it.</summary>
    protected static RectInt32 CentredOn(RectInt32 nearby, int width, int height) =>
        new(
            nearby.X + ((nearby.Width - width) / 2),
            nearby.Y + ((nearby.Height - height) / 2),
            width,
            height);

    /// <summary>
    /// Whether a rectangle overlaps the working area of some display. A monitor that has been
    /// unplugged since the position was recorded is the case this catches.
    /// </summary>
    protected static bool FitsOnADisplay(int x, int y, int width, int height)
    {
        var area = DisplayArea.GetFromRect(new RectInt32(x, y, width, height), DisplayAreaFallback.Nearest);

        RectInt32 bounds = area.WorkArea;

        return x < bounds.X + bounds.Width
            && y < bounds.Y + bounds.Height
            && x + width > bounds.X
            && y + height > bounds.Y;
    }

    // ------------------------------------------------------------------ interop

    private const int GwlpHwndParent = -8;

    /// <summary>
    /// DllImport rather than the source-generated LibraryImport: the generator emits unsafe
    /// code, and this project does not allow it.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
