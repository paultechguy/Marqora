// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.App.Views;

/// <summary>
/// Help, About.
///
/// Built in code rather than XAML because it is a one-off with no bindings, and because the
/// version and runtime lines are read at runtime rather than written into markup.
/// </summary>
internal sealed class AboutDialog : ContentDialog
{
    /// <summary>
    /// Marqora's own licence, as opposed to the third-party ones listed below. The full text
    /// ships as LICENSE at the root of the repository and is what the link below opens; the
    /// same pair appears in every source file as an SPDX header.
    ///
    /// The address itself comes from <see cref="ProjectLinks"/>, which is where every URL
    /// pointing at the project is written down, once.
    /// </summary>
    private const string Licence = "Apache-2.0";

    /// <summary>
    /// The copyright, read from the assembly rather than written here, so the Copyright
    /// property in Directory.Build.props stays the one place it is stated. The fallback is
    /// only reached if that property is dropped, and an About box should still open.
    /// </summary>
    private static readonly string Copyright =
        typeof(AboutDialog).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
            ?? "Copyright (c) Paul Carver";

    private static readonly (string Name, string Version, string Licence)[] ThirdParty =
    [
        ("Monaco Editor", "0.56.0", "MIT"),
        ("Mermaid", "11.17.0", "MIT"),
        ("KaTeX", "0.18.4", "MIT"),
        ("highlight.js", "11.12.0", "BSD-3-Clause"),
        ("Markdig", "1.3.2", "BSD-2-Clause"),
        ("Serilog", "4.4.0", "Apache-2.0"),
    ];

    public AboutDialog(IAppPaths paths, ILogger logger)
    {
        Title = "About Marqora";
        PrimaryButtonText = "Copy details";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.Close;

        string version = AppVersion.Current;
        string runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        // The primary button copies rather than closes, which is what a bug report needs.
        PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            CopyDetails(version, runtime, paths);
        };

        Content = BuildContent(version, runtime, paths, logger);
    }

    private static ScrollViewer BuildContent(
        string version,
        string runtime,
        IAppPaths paths,
        ILogger logger)
    {
        var panel = new StackPanel { Spacing = 16, Width = 420 };

        panel.Children.Add(BuildHeader(version));

        panel.Children.Add(new TextBlock
        {
            // The last sentence earns its place now that a menu item one line above this
            // dialog offers to open GitHub. Marqora still opens no socket of its own - a
            // link is handed to the shell, the same as the folder rows below - but the
            // claim reads better with the one thing that could look like an exception
            // stated rather than left for a sceptical reader to catch.
            Text = "A Markdown viewer and editor for Windows 11. Everything runs locally: "
                + "no network calls, no telemetry, no account. Links you open are handed "
                + "to your browser rather than fetched here.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });

        panel.Children.Add(BuildFacts(runtime, paths, logger));
        panel.Children.Add(BuildThirdParty());

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 460,
        };
    }

    private static StackPanel BuildHeader(string version)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };

        // The logo, falling back to the lettered tile if the artwork is missing from the
        // deployment. A dialog whose job is to report what you are running should still open.
        if (AppImages.Logo(88) is { } logo)
        {
            row.Children.Add(new Image { Source = logo, Width = 44, Height = 44 });
        }
        else
        {
            row.Children.Add(new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new CornerRadius(10),
                Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
                Child = new TextBlock
                {
                    Text = "M",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = "Marqora", FontSize = 20, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = $"Version {version}", Opacity = 0.7, FontSize = 12.5 });
        text.Children.Add(new TextBlock { Text = Copyright, Opacity = 0.55, FontSize = 11.5 });

        row.Children.Add(text);
        return row;
    }

    private static Grid BuildFacts(string runtime, IAppPaths paths, ILogger logger)
    {
        var grid = new Grid { ColumnSpacing = 14, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddRow(grid, 0, "Author", "Paul Carver");
        AddLinkRow(grid, 1, "License", Licence, ProjectLinks.LicenceUrl, logger);
        AddRow(grid, 2, "Runtime", runtime);
        AddPathRow(grid, 3, "Data folder", paths.DataDirectory);
        AddPathRow(grid, 4, "Logs", paths.LogDirectory);
        AddPathRow(grid, 5, "Snippets", paths.SnippetsDirectory);

        return grid;
    }

    private static void AddRow(Grid grid, int row, string label, string value)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock { Text = label, Opacity = 0.65, FontSize = 12.5 };
        Grid.SetRow(name, row);
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        var content = new TextBlock
        {
            Text = value,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };

        Grid.SetRow(content, row);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
    }

    /// <summary>
    /// A folder row: the same as <see cref="AddRow"/>, but the path opens in Explorer.
    ///
    /// The path is still shown in full rather than behind a friendly word, because the other
    /// half of what this row is for is reading the path out into a bug report. Selecting the
    /// text is given up in exchange — a HyperlinkButton takes the click — but Copy details
    /// carries every one of these anyway.
    /// </summary>
    private static void AddPathRow(Grid grid, int row, string label, string path)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock { Text = label, Opacity = 0.65, FontSize = 12.5 };
        Grid.SetRow(name, row);
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        // A Hyperlink inline rather than a HyperlinkButton: a button wraps its content in a
        // presenter, so a TextBlock inside one keeps its own foreground and the path ends up
        // looking like ordinary text. This keeps the link coloring, the wrapping these long
        // paths need, and the same metrics as every other row.
        var link = new Hyperlink();
        link.Inlines.Add(new Run { Text = path });
        link.Click += (_, _) => OpenFolder(path);

        var content = new TextBlock
        {
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
        };

        content.Inlines.Add(link);
        ToolTipService.SetToolTip(content, $"Open {path} in File Explorer");

        Grid.SetRow(content, row);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
    }

    /// <summary>
    /// A row whose value opens a web page in the default browser.
    ///
    /// The app itself still makes no network calls: this hands a URL to the shell, the same
    /// way the folder rows hand it a path, and nothing is fetched unless the reader asks for
    /// it. Only the short licence name is shown; the URL lives in
    /// <see cref="ProjectLinks.LicenceUrl"/> and in the tooltip, so the row stays narrow
    /// next to the labels around it.
    /// </summary>
    private static void AddLinkRow(
        Grid grid,
        int row,
        string label,
        string text,
        string url,
        ILogger logger)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock { Text = label, Opacity = 0.65, FontSize = 12.5 };
        Grid.SetRow(name, row);
        Grid.SetColumn(name, 0);
        grid.Children.Add(name);

        var link = new Hyperlink();
        link.Inlines.Add(new Run { Text = text });
        link.Click += (_, _) => _ = ExternalLink.OpenAsync(url, logger);

        var content = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        content.Inlines.Add(link);
        ToolTipService.SetToolTip(content, $"Open {url}");

        Grid.SetRow(content, row);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);
    }

    /// <summary>
    /// Opens one of the app's own folders.
    ///
    /// Created first if it is missing: these are all folders Marqora owns, and a launch that
    /// silently does nothing because the folder has not been written to yet is worse than
    /// making it. A failure past that is swallowed — this is an About box, and there is
    /// nothing useful to say to someone whose shell refused to open a directory.
    /// </summary>
    private static async void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);

            await Windows.System.Launcher.LaunchFolderPathAsync(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private static StackPanel BuildThirdParty()
    {
        var panel = new StackPanel { Spacing = 6 };

        panel.Children.Add(new TextBlock
        {
            Text = "Built with",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
        });

        var list = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };

        foreach ((string name, string componentVersion, string licence) in ThirdParty)
        {
            list.Inlines.Add(new Run { Text = $"{name} {componentVersion} ({licence})" });
            list.Inlines.Add(new LineBreak());
        }

        panel.Children.Add(list);
        return panel;
    }

    private static void CopyDetails(string version, string runtime, IAppPaths paths)
    {
        string details = string.Join(
            Environment.NewLine,
            $"Marqora {version}",
            Copyright,
            $"License: {Licence} ({ProjectLinks.LicenceUrl})",
            $"Runtime: {runtime}",
            $"OS: {Environment.OSVersion.VersionString}",
            $"Data folder: {paths.DataDirectory}",
            $"Logs: {paths.LogDirectory}",
            $"Snippets: {paths.SnippetsDirectory}");

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(details);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

}
