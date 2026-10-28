// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.WebRender;

/// <summary>
/// The shell's <c>applyPreferences</c> message, built once for both hosts. The live preview sends
/// it whenever Preferences change; the headless host before a render, so a document it draws for
/// an export is drawn with the reader's own settings, separated diagram labels among them.
/// </summary>
public static class ShellPreferences
{
    public const string Message = "applyPreferences";

    public static object Payload(PreviewPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        return new
        {
            // Null font families are sent as null rather than omitted: the web side clears its
            // own custom property on null, which is what restores the stylesheet's stack.
            sourceFont = preferences.SourceFontFamily,
            sourceFontSize = preferences.SourceFontSize,
            previewFont = preferences.PreviewFontFamily,
            previewFontSize = preferences.PreviewFontSize,
            previewMaxWidth = preferences.PreviewMaxWidth,
            tabSize = preferences.TabSize,
            insertSpaces = preferences.InsertSpaces,
            minimap = preferences.ShowMinimap,
            highlightCurrentLine = preferences.HighlightCurrentLine,
            continueLists = preferences.ContinueLists,
            repeatListNumbers = preferences.RepeatListNumbers,
            autoCloseBrackets = preferences.AutoCloseBrackets,
            separateDiagramLabels = preferences.SeparateDiagramLabels,

            // Heading numbering is deliberately not here. The numbers are written into the HTML
            // by the renderer, so the shell has nothing to do with them.
        };
    }
}
