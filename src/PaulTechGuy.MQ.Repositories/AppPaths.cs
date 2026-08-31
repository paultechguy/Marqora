// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// Default <see cref="IAppPaths"/>: per-user state under %LOCALAPPDATA%\PaulTechGuy\Marqora,
/// and read-only web assets deployed alongside the executable.
/// </summary>
public sealed class AppPaths : IAppPaths
{
    private const string CompanyFolder = "PaulTechGuy";
    private const string ProductFolder = "Marqora";

    /// <summary>
    /// The welcome document's name, the same on both sides of the copy and the same for every
    /// release. It is what the tab is labelled with, so it reads as a title rather than as a
    /// file name.
    /// </summary>
    private const string WelcomeFileName = "Welcome to Marqora.md";

    public AppPaths()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppContext.BaseDirectory)
    {
    }

    /// <summary>Test seam: point both roots at a temporary directory.</summary>
    public AppPaths(string localAppDataRoot, string installRoot)
    {
        DataDirectory = Path.Combine(localAppDataRoot, CompanyFolder, ProductFolder);
        WebAssetsDirectory = Path.Combine(installRoot, "Assets", "web");
        WelcomeTemplatePath = Path.Combine(installRoot, "Assets", WelcomeFileName);
    }

    public string DataDirectory { get; }

    public string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");

    public string RecentFilesFilePath => Path.Combine(DataDirectory, "recent-files.json");

    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    public string SnippetsDirectory => Path.Combine(DataDirectory, "snippets");

    public string WebAssetsDirectory { get; }

    public string WelcomeTemplatePath { get; }

    public string WelcomeDocumentPath => Path.Combine(DataDirectory, WelcomeFileName);

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(SnippetsDirectory);
    }
}
