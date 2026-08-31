// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Rendering;

/// <summary>Describes the on-disk web assets the preview host maps into the WebView.</summary>
public interface IWebAssetProvider
{
    /// <summary>Virtual host name the WebView maps the root directory to.</summary>
    string VirtualHostName { get; }

    /// <summary>Absolute path to the folder containing shell.html, app.js and vendor assets.</summary>
    string RootDirectory { get; }

    /// <summary>Absolute URI of the shell document to navigate to.</summary>
    Uri ShellUri { get; }

    /// <summary>Absolute URI of the page shown in the floating cheatsheet window.</summary>
    Uri CheatsheetUri { get; }

    /// <summary>Absolute path to the markdown source the cheatsheet window renders.</summary>
    string CheatsheetSourcePath { get; }

    /// <summary>Absolute URI of the page a single diagram is popped out into.</summary>
    Uri DiagramUri { get; }

    /// <summary>
    /// True when the required assets are present. False means the vendor bundle was never
    /// restored, and the caller should surface a helpful message instead of a blank view.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Names of any required assets that are missing, for diagnostics.</summary>
    IReadOnlyList<string> MissingAssets { get; }
}
