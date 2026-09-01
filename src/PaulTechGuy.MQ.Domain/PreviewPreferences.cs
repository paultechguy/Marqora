// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The slice of <see cref="AppSettings"/> the web surface actually needs, sent to it in one
/// message rather than one message per setting.
///
/// The host already has a method per toggle for the settings the View menu owns
/// (SetWordWrap, SetLineNumbers and so on) and those stay as they are: they are wired, they
/// are used, and each is a single call from a menu handler. This record exists for the other
/// direction — a dozen values that only ever change together, from the preferences dialog,
/// and that would otherwise have added a dozen near-identical methods to IPreviewHost.
/// </summary>
public sealed record PreviewPreferences
{
    /// <summary>
    /// Font for the source pane, or null to keep the stylesheet's own stack.
    ///
    /// Null rather than a hardcoded default name: the stylesheet's stack has fallbacks in it
    /// for a machine without Cascadia Code, and repeating the head of that stack here would
    /// throw the fallbacks away.
    /// </summary>
    public string? SourceFontFamily { get; init; }

    /// <summary>Base size of the source pane in CSS pixels, before zoom is applied.</summary>
    public int SourceFontSize { get; init; } = TypographyDefaults.SourceFontSize;

    /// <summary>Font for the rendered preview, or null to keep the stylesheet's own stack.</summary>
    public string? PreviewFontFamily { get; init; }

    /// <summary>Base size of the preview in CSS pixels, before zoom is applied.</summary>
    public double PreviewFontSize { get; init; } = TypographyDefaults.PreviewFontSize;

    /// <summary>
    /// Widest the rendered column gets, in CSS pixels, or zero for no limit.
    ///
    /// Zero by default, and deliberately so. The preview used to be capped at a 46em measure
    /// and that cap was removed on purpose - see the comment above .mq-preview in app.css -
    /// because it left a wide band of empty background down both sides of the pane. Offering
    /// the cap as a preference is not the same as bringing it back: nothing changes for
    /// anyone who does not go looking for it.
    /// </summary>
    public int PreviewMaxWidth { get; init; }

    public int TabSize { get; init; } = 4;

    /// <summary>Insert spaces when Tab is pressed, rather than a tab character.</summary>
    public bool InsertSpaces { get; init; } = true;

    public bool ShowMinimap { get; init; }

    public bool HighlightCurrentLine { get; init; } = true;

    /// <summary>Carry a list marker onto the next line when Enter is pressed.</summary>
    public bool ContinueLists { get; init; } = true;

    /// <summary>Close a bracket, quote or emphasis marker as it is typed.</summary>
    public bool AutoCloseBrackets { get; init; } = true;

    public HeadingNumbering HeadingNumbering { get; init; }

    /// <summary>Everything the web surface needs, read off the settings record.</summary>
    public static PreviewPreferences FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new PreviewPreferences
        {
            SourceFontFamily = settings.SourceFontFamily,
            SourceFontSize = settings.SourceFontSize,
            PreviewFontFamily = settings.PreviewFontFamily,
            PreviewFontSize = settings.PreviewFontSize,
            PreviewMaxWidth = settings.PreviewMaxWidth,
            TabSize = settings.TabSize,
            InsertSpaces = settings.InsertSpaces,
            ShowMinimap = settings.ShowMinimap,
            HighlightCurrentLine = settings.HighlightCurrentLine,
            ContinueLists = settings.ContinueLists,
            AutoCloseBrackets = settings.AutoCloseBrackets,
            HeadingNumbering = settings.HeadingNumbering,
        };
    }

}

/// <summary>
/// The typography the app has always shipped, stated once.
///
/// These numbers were previously spelled out in app.css and app.js alone. They are needed in
/// C# now as the defaults for the matching preferences, and a second copy that could drift
/// from the stylesheet is exactly the sort of thing that goes wrong quietly, so the C# side
/// is the declaration and the web side is told what to use at startup.
/// </summary>
public static class TypographyDefaults
{
    /// <summary>Matches SOURCE_BASE_FONT_PX in app.js.</summary>
    public const int SourceFontSize = 14;

    /// <summary>Matches --mq-preview-base in app.css.</summary>
    public const double PreviewFontSize = 15.5;

    /// <summary>Zero means the preview fills its pane, which is what it has always done.</summary>
    public const int UnlimitedPreviewWidth = 0;

    public const int MinimumFontSize = 8;

    public const int MaximumFontSize = 48;

    public const int MinimumPreviewWidth = 480;

    public const int MaximumPreviewWidth = 2000;
}
