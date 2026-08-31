// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Rendering;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// Points the preview host at the web assets deployed next to the executable and reports
/// clearly when the vendor bundle was never restored, which is otherwise a blank window.
/// </summary>
public sealed class WebAssetProvider : IWebAssetProvider
{
    /// <summary>
    /// A virtual host keeps the shell on a real https origin. Loading from file:// would put
    /// the page in an opaque origin, which blocks the module and worker loads Monaco needs.
    /// </summary>
    public const string DefaultVirtualHostName = "marqora.assets";

    private const string CheatsheetPage = "cheatsheet.html";

    /// <summary>
    /// The pop-out viewer. It is handed a finished SVG and never loads mermaid itself, so it
    /// needs none of the vendor bundle.
    /// </summary>
    private const string DiagramPage = "diagram.html";

    /// <summary>
    /// The cheatsheet is shipped as markdown and rendered through the ordinary pipeline
    /// rather than as pre-built HTML, so it cannot disagree with the preview about what
    /// markdown looks like, and editing it later is editing a document.
    /// </summary>
    private const string CheatsheetSource = "cheatsheet.md";

    private static readonly string[] RequiredAssets =
    [
        "shell.html",
        "app.js",
        "app.css",
        "mermaid-frame.html",
        "mermaid-frame.js",
        CheatsheetPage,
        "cheatsheet.js",
        "cheatsheet.css",
        CheatsheetSource,
        DiagramPage,
        "diagram.js",
        "diagram.css",
        Path.Combine("vendor", "monaco", "vs", "loader.js"),
        Path.Combine("vendor", "mermaid", "mermaid.esm.min.mjs"),
        Path.Combine("vendor", "katex", "katex.min.js"),
        Path.Combine("vendor", "highlight", "highlight.min.js"),
        Path.Combine("vendor", "katex", "katex.min.css"),
    ];

    public WebAssetProvider(IAppPaths paths, ILogger<WebAssetProvider> logger)
    {
        RootDirectory = paths.WebAssetsDirectory;

        MissingAssets = [.. RequiredAssets.Where(asset => !File.Exists(Path.Combine(RootDirectory, asset)))];

        if (MissingAssets.Count > 0)
        {
            logger.LogError(
                "Web assets are incomplete in {Root}. Missing: {Missing}. Run build/Get-WebAssets.ps1.",
                RootDirectory,
                string.Join(", ", MissingAssets));
        }
    }

    public string VirtualHostName => DefaultVirtualHostName;

    public string RootDirectory { get; }

    public Uri ShellUri => new($"https://{VirtualHostName}/shell.html");

    public Uri CheatsheetUri => new($"https://{VirtualHostName}/{CheatsheetPage}");

    public string CheatsheetSourcePath => Path.Combine(RootDirectory, CheatsheetSource);

    public Uri DiagramUri => new($"https://{VirtualHostName}/{DiagramPage}");

    public bool IsAvailable => MissingAssets.Count == 0;

    public IReadOnlyList<string> MissingAssets { get; }
}
