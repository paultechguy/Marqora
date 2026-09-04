// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using Windows.System;

namespace PaulTechGuy.MQ.App.ViewModels;

/// <summary>
/// What the preferences dialog talks to. One object, so the dialog never has to know which
/// settings are owned by the shell, which by the web surface, and which by neither.
///
/// The split matters, and it is the reason this class exists rather than the dialog writing
/// to <see cref="ISettingsService"/> itself:
///
///   - Eight settings are also on the View menu. Those are delegated to
///     <see cref="MainViewModel"/>, whose properties are what the menu's check marks are
///     bound to and what pushes the value to the editor. Writing them anywhere else would
///     save the value and leave both the tick and the running editor stale.
///   - The typography and editor settings belong to the web surface, and go down in one
///     message once the settings record has been updated.
///   - The rest - startup, saving, recent files, export defaults, log retention - are read
///     when they are next needed, so saving them is the whole of the work.
///
/// Nothing here is an observable property. The dialog's controls are the only view of this
/// state, they are populated once when it opens, and every change is applied as it is made -
/// so there is no second view that would have to be told.
/// </summary>
public sealed class PreferencesViewModel(
    MainViewModel main,
    ISettingsService settings,
    IAppPaths paths,
    IFileDialogService files,
    IPreferencesTransfer transfer,
    IThemeService theme,
    ILogger<PreferencesViewModel> logger)
{
    /// <summary>
    /// Light or Dark as it stands, never System.
    ///
    /// The dialog needs this for the parts of itself the window's theme does not reach. A
    /// ContentDialog and its flyouts live in the popup root, a sibling of the window's
    /// content rather than a child of it, so nothing there hears about a theme change - and
    /// this dialog is the one place the theme is changed from.
    /// </summary>
    public AppTheme EffectiveTheme => theme.Effective;

    /// <summary>Raised when the theme actually in force changes, including from Windows.</summary>
    public event EventHandler<AppTheme>? EffectiveThemeChanged
    {
        add => theme.EffectiveThemeChanged += value;
        remove => theme.EffectiveThemeChanged -= value;
    }

    /// <summary>
    /// The settings as they were when the dialog opened.
    ///
    /// This is the parachute. Preferences apply as they are made, which is what lets someone
    /// see a font or a theme before committing to it - but it left no way back, and a dialog
    /// whose Escape key keeps your changes is lying about what Escape means. Cancel restores
    /// this.
    ///
    /// Captured in the field initialiser, so it is taken once when the dialog's view model is
    /// built and cannot drift afterwards.
    /// </summary>
    private readonly AppSettings _opening = settings.Current;

    /// <summary>The settings as they stand. Read afresh each time; every change is live.</summary>
    public AppSettings Current => settings.Current;

    /// <summary>What the dialog opened on, for the controls that only commit on OK.</summary>
    public AppSettings Opening => _opening;

    /// <summary>
    /// True when something applied live differs from how the dialog opened.
    ///
    /// Compared as preferences rather than as whole records: the app goes on recording things
    /// about itself while the dialog is up - where the window is, most obviously - and a
    /// window nudged by a pixel is not a reason to ask someone whether they meant to change
    /// their settings.
    /// </summary>
    public bool HasLiveChanges => _opening.WithSessionOf(settings.Current) != settings.Current;

    /// <summary>
    /// The font stacks the stylesheet falls back on, for the dialog to show beside its
    /// "(default)" entry. Null if the preview has not finished starting.
    /// </summary>
    public string? DefaultSourceFont => main.DefaultSourceFont;

    public string? DefaultPreviewFont => main.DefaultPreviewFont;

    /// <summary>
    /// The single font each pane is actually drawn in.
    ///
    /// Not the same question as which font was chosen. A stack names fonts the machine may
    /// not have, and a name typed into the box may not be installed at all - in which case
    /// nothing on screen changes and the dialog would otherwise have no way to say so.
    /// </summary>
    public string? ResolvedSourceFont => main.ResolvedSourceFont;

    public string? ResolvedPreviewFont => main.ResolvedPreviewFont;

    /// <summary>
    /// Shows both panes while the dialog is up, so a setting that only shows in one of them
    /// can still be seen taking effect. Does not become the remembered view mode.
    /// </summary>
    public Task ShowBothPanesAsync() => main.ShowBothPanesAsync();

    /// <summary>Puts the panes back to the mode the settings name, however the dialog closed.</summary>
    public Task RestoreViewModeAsync() => main.RestoreViewModeAsync();

    /// <summary>Raised when the shell has re-measured which fonts are in use.</summary>
    public event EventHandler? FontsResolved
    {
        add => main.FontsResolved += value;
        remove => main.FontsResolved -= value;
    }

    // ------------------------------------------------------- mirrored on the View menu

    public Task SetWordWrapAsync(bool value) => main.SetWordWrapAsync(value);

    public Task SetLineNumbersAsync(bool value) => main.SetLineNumbersAsync(value);

    public Task SetShowWhitespaceAsync(bool value) => main.SetShowWhitespaceAsync(value);

    public Task SetWrapGlyphAsync(bool value) => main.SetWrapGlyphAsync(value);

    public Task SetScrollSyncAsync(bool value) => main.SetScrollSyncAsync(value);

    public Task SetDiagnosticsAsync(bool value) => main.SetDiagnosticsAsync(value);

    public Task SetShowOutlineAsync(bool value) => main.SetShowOutlineAsync(value);

    public void SetOutlineMaxDepth(int value) => main.SetOutlineMaxDepth(value);

    public void SetReloadOnExternalChange(bool value) => main.SetReloadOnExternalChange(value);

    public void SetTheme(AppTheme theme) => main.ApplyTheme(theme);

    // ------------------------------------------------------------------ everything else

    /// <summary>
    /// Saves a change and pushes it to the preview.
    ///
    /// The push is unconditional rather than worked out per setting. It is one posted
    /// message that the shell applies in a single pass, so telling it about a change it does
    /// not care about costs less than the code to decide which changes it cares about.
    /// </summary>
    public async Task UpdateAsync(Func<AppSettings, AppSettings> mutate)
    {
        settings.Update(mutate);

        await main.ApplyPreviewPreferencesAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Commits the settings the dialog deliberately did not apply as they were changed.
    ///
    /// A few preferences do something the moment they take effect, and cannot be taken back
    /// by putting the number in the settings file back: a smaller recent-files limit drops
    /// entries on the next file opened, and autosave writes to disk. Applying those live
    /// would make Cancel a promise the dialog could not keep, so they wait for OK.
    ///
    /// They are also, conveniently, the ones with nothing to preview - so nothing is lost by
    /// making them wait, and the preview never has to hear about them.
    /// </summary>
    public void CommitDeferred(Func<AppSettings, AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        settings.Update(mutate);
    }

    /// <summary>
    /// Puts every preference back to how the dialog found it. What Cancel does.
    ///
    /// This is why applying live is safe: everything the dialog can change lives in the
    /// settings record, and the record was copied on the way in. Restoring defaults is
    /// undone by this too - it is an ordinary change like any other until OK is pressed.
    /// </summary>
    public Task RevertAsync() => RestoreAsync(_opening);

    /// <summary>
    /// Puts every preference back to its default, leaving the session's record of itself -
    /// the open documents, the window, the search history - alone. See
    /// <see cref="AppSettings.ResetPreferences"/>.
    /// </summary>
    public Task ResetAsync() => RestoreAsync(AppSettings.ResetPreferences(settings.Current));

    /// <summary>
    /// Makes <paramref name="target"/> the settings in force, keeping the session's own
    /// record of itself.
    ///
    /// The mirrored settings are re-applied one by one through the same setters the dialog
    /// uses, because writing the record is only half the job: those setters own the parts of
    /// the app the record does not reach - the View menu's check marks, the editor's options
    /// and the theme actually painted on screen. Each one is idempotent, so the ones that did
    /// not change cost nothing.
    /// </summary>
    private async Task RestoreAsync(AppSettings target)
    {
        AppSettings restored = target.WithSessionOf(settings.Current);

        settings.Update(_ => restored);

        SetTheme(restored.Theme);
        SetReloadOnExternalChange(restored.ReloadOnExternalChange);

        await main.SetWordWrapAsync(restored.WordWrapEnabled).ConfigureAwait(true);
        await main.SetLineNumbersAsync(restored.ShowLineNumbers).ConfigureAwait(true);
        await main.SetShowWhitespaceAsync(restored.ShowWhitespace).ConfigureAwait(true);
        await main.SetWrapGlyphAsync(restored.ShowWrapGlyph).ConfigureAwait(true);
        await main.SetScrollSyncAsync(restored.ScrollSyncEnabled).ConfigureAwait(true);
        await main.SetDiagnosticsAsync(restored.ShowDiagnostics).ConfigureAwait(true);

        SetOutlineMaxDepth(restored.OutlineMaxDepth);
        await SetShowOutlineAsync(restored.ShowOutline).ConfigureAwait(true);

        await main.ApplyPreviewPreferencesAsync().ConfigureAwait(true);
    }

    // ------------------------------------------------------------ moving between machines

    /// <summary>
    /// Writes <paramref name="snapshot"/> to a file the user picks, and says whether it
    /// landed and what to tell the user. Null when the dialog was cancelled, which needs no
    /// report at all.
    ///
    /// Whether it worked is returned rather than left to be inferred from the wording: the
    /// dialog colours the report by outcome, and deciding that by reading the message back
    /// would break the first time the message was reworded.
    ///
    /// The settings are passed in rather than read off <see cref="Current"/>, because four of
    /// them are still sitting in the dialog's controls at this point and have deliberately not
    /// been written down yet - see <see cref="CommitDeferred"/>. Exporting what the settings
    /// record holds would quietly write the old autosave and recent-files values into a file
    /// the user believes shows what is on their screen.
    /// </summary>
    public async Task<(bool Succeeded, string Message)?> ExportAsync(AppSettings snapshot)
    {
        string? path = await files.PickExportFileAsync(
            PreferencesDocument.SuggestedFileName(DateTimeOffset.Now),
            PreferencesFilterLabel,
            [PreferencesDocument.FileExtension]).ConfigureAwait(true);

        if (path is null)
        {
            return null;
        }

        try
        {
            await transfer.ExportAsync(path, snapshot).ConfigureAwait(true);

            return (true, $"Your preferences were written to {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Preferences could not be exported to {Path}.", path);

            return (false, $"The file could not be written. {ex.Message}");
        }
    }

    /// <summary>
    /// Reads a preferences file the user picks. Null when the dialog was cancelled.
    ///
    /// Nothing is applied here. The result carries the settings it worked out and the caller
    /// decides what to do with them, which is what keeps an import an ordinary change to the
    /// dialog - visible straight away, and undone by Cancel like any other.
    /// </summary>
    public async Task<PreferencesImportResult?> ImportAsync()
    {
        string? path = await files.PickImportFileAsync(
            "Import Marqora preferences",
            PreferencesFilterLabel,
            [PreferencesDocument.FileExtension]).ConfigureAwait(true);

        return path is null
            ? null
            : await transfer.ImportAsync(path, settings.Current).ConfigureAwait(true);
    }

    /// <summary>
    /// Puts imported preferences into force, through the same path as Cancel and Restore
    /// Defaults - so the View menu's check marks, the editor's options and the theme on screen
    /// all move with them.
    /// </summary>
    public Task ApplyImportedAsync(AppSettings imported) => RestoreAsync(imported);

    /// <summary>What the file dialogs call this kind of file in their type list.</summary>
    private const string PreferencesFilterLabel = "Marqora preferences";

    /// <summary>
    /// Opens the folder holding settings.json in File Explorer.
    ///
    /// Flushed first, so what the user finds there is what the app currently holds rather
    /// than what it held up to three quarters of a second ago - the debounce that keeps a
    /// splitter drag off the disk would otherwise make the file look stale to anyone who
    /// went looking at it straight after changing something.
    /// </summary>
    public async Task OpenSettingsFolderAsync()
    {
        try
        {
            await settings.FlushAsync().ConfigureAwait(true);
            await Launcher.LaunchFolderPathAsync(paths.DataDirectory);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not open the settings folder.");
        }
    }
}
