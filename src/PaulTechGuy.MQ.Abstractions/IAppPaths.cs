// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions;

/// <summary>
/// Resolves every location the app writes to. Injecting this keeps %LOCALAPPDATA%
/// out of the rest of the codebase and lets tests point at a temp directory.
/// </summary>
public interface IAppPaths
{
    /// <summary>Root per-user data directory, created on demand.</summary>
    string DataDirectory { get; }

    string SettingsFilePath { get; }

    string RecentFilesFilePath { get; }

    string LogDirectory { get; }

    /// <summary>Folder holding the user's own snippet files, one snippet per file.</summary>
    string SnippetsDirectory { get; }

    /// <summary>Folder containing the bundled preview web assets, shipped next to the executable.</summary>
    string WebAssetsDirectory { get; }

    /// <summary>
    /// The stock welcome document as it ships, beside the executable. Read-only: the install
    /// folder is not necessarily writable, so this is copied before it is opened.
    /// </summary>
    string WelcomeTemplatePath { get; }

    /// <summary>
    /// The user's own copy of the welcome document, refreshed from the template by each new
    /// release. Its name is fixed, so a release replaces the last one rather than leaving a
    /// trail of them behind.
    /// </summary>
    string WelcomeDocumentPath { get; }

    /// <summary>Ensures the writable directories exist.</summary>
    void EnsureCreated();
}
