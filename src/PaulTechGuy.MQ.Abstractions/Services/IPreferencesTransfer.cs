// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// Moves preferences between machines as a file.
///
/// Both halves are deliberately pure: export is handed the settings to write rather than
/// reaching for them, and import returns the settings it worked out rather than putting them
/// into force. That is what lets the preferences dialog treat an import as an ordinary
/// change - applied through the same path as every other, and undone by Cancel like every
/// other - instead of a side effect that had already happened by the time it was reported.
/// </summary>
public interface IPreferencesTransfer
{
    /// <summary>
    /// Writes <paramref name="settings"/> to <paramref name="path"/>, without the session's
    /// own record of itself. Throws if the file cannot be written; the caller reports it.
    /// </summary>
    Task ExportAsync(string path, AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a preferences file and merges it onto <paramref name="current"/>.
    ///
    /// Never throws for anything to do with the file itself: an unreadable, malformed or
    /// unrelated file comes back as a result carrying the reason, because "this is not a
    /// preferences file" is something to tell the user rather than something to log.
    ///
    /// Settings the file does not mention keep the value they have on this machine. That is
    /// the friendlier reading of a file from an older build, which cannot have an opinion
    /// about a preference that did not exist when it was written.
    /// </summary>
    Task<PreferencesImportResult> ImportAsync(
        string path,
        AppSettings current,
        CancellationToken cancellationToken = default);
}
