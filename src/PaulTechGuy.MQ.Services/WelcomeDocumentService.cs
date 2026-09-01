// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Decides whether this launch should open the welcome document, and if it should, refreshes
/// the user's copy from the one that shipped.
///
/// Two things ask for it: the first launch of a version, and a launch that held Shift. The
/// second is the way back to a document that has already introduced itself - and, because the
/// copy is rewritten either way, the way to undo a scribble on it.
///
/// The version is recorded as soon as the copy lands rather than after the tab opens. A
/// document that was written to disk but could not be opened is a failure worth one entry in
/// the log, not a document that reintroduces itself on every launch afterwards.
/// </summary>
public sealed class WelcomeDocumentService(
    IAppPaths paths,
    ISettingsService settings,
    string appVersion,
    bool wasRequested,
    ILogger<WelcomeDocumentService> logger) : IWelcomeDocumentService
{
    public bool WasRequested { get; } = wasRequested;

    public string DocumentPath => paths.WelcomeDocumentPath;

    public async Task<string?> PrepareAsync(CancellationToken cancellationToken = default)
    {
        string? shown = settings.Current.LastWelcomeVersion;
        bool isFirstRun = !string.Equals(shown, appVersion, StringComparison.Ordinal);

        if (!isFirstRun && !WasRequested)
        {
            logger.LogDebug("The welcome document has already been shown for {Version}.", appVersion);
            return null;
        }

        string source = paths.WelcomeTemplatePath;

        if (!File.Exists(source))
        {
            // Nothing is recorded, so the document still appears once the file is there. A
            // build with an incomplete deployment should not cost the user the introduction.
            logger.LogWarning("The welcome document is missing from the deployment: {Path}.", source);
            return null;
        }

        string destination = paths.WelcomeDocumentPath;

        try
        {
            await CopyAsync(source, destination, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not refresh the welcome document at {Path}.", destination);
            return null;
        }

        settings.Update(s => s with { LastWelcomeVersion = appVersion });

        logger.LogInformation(
            "The welcome document is ready at {Path} ({Reason}).",
            destination,
            WasRequested ? "asked for with Shift" : $"first run of {appVersion}");

        return destination;
    }

    /// <summary>
    /// Overwrites the user's copy with the shipped one.
    ///
    /// Copied by hand rather than with <see cref="File.Copy(string, string, bool)"/>, which
    /// carries the source file's attributes across. An installed copy marked read-only would
    /// otherwise produce a document that cannot be saved, and a destination left read-only by
    /// an earlier release would refuse the next copy outright.
    /// </summary>
    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            var existing = new FileInfo(destination);

            if (existing.IsReadOnly)
            {
                existing.IsReadOnly = false;
            }
        }

        await using FileStream input = File.OpenRead(source);
        await using FileStream output = File.Create(destination);

        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }
}
