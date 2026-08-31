// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// The stock document that introduces the app, shown once per release.
///
/// The master ships read-only beside the executable and is copied into the user's data
/// folder before it is opened, so the tab points at a file that can be edited and saved.
/// The copy is overwritten by each new release, which is what makes the document a
/// description of the version actually in front of the user rather than of whichever one
/// installed it first.
/// </summary>
public interface IWelcomeDocumentService
{
    /// <summary>
    /// True when this launch asked for the document outright - Shift held as the app starts -
    /// rather than being offered it because the version changed. An explicit request is worth
    /// distinguishing: it also wins the focus from a file named on the command line, which
    /// the automatic showing deliberately does not.
    /// </summary>
    bool WasRequested { get; }

    /// <summary>
    /// Puts a current copy of the welcome document in place if this build has not been run
    /// before, or if this launch asked for it, and returns the path to open. Returns null
    /// when there is nothing to show, or when the shipped master is missing.
    ///
    /// Call this before restoring the previous session: the restored tabs may include the
    /// welcome document from an earlier release, and it should come back holding this
    /// release's text rather than being reloaded from underneath a moment later.
    /// </summary>
    Task<string?> PrepareAsync(CancellationToken cancellationToken = default);
}
