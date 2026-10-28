// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Cli.Rendering;
using PaulTechGuy.MQ.Repositories;
using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.Cli;

/// <summary>
/// Everything a run reads from or writes to the world outside the files it is given: its two
/// output streams, standard input, whether stderr is a terminal, the one environment variable it
/// honors, and the folder it was started in.
///
/// Gathered in one place so a test can hand a run its own and read back exactly what it wrote,
/// without touching the process's console - which every test in the assembly shares.
/// </summary>
internal sealed record CliEnvironment(
    TextWriter Out,
    TextWriter Error,
    Func<Stream> OpenStandardInput,
    bool ErrorIsTerminal,
    Func<string, string?> GetVariable,
    string CurrentDirectory)
{
    /// <summary>
    /// Where the app keeps its files, for --settings app. The user's own by default; a test points
    /// it at a folder of its own, so it never reads the Preferences of the machine it runs on.
    /// </summary>
    public Func<IAppPaths> AppPaths { get; init; } = () => new AppPaths();

    /// <summary>
    /// The color themes: the ones embedded in the Themes library, which are the app's. A test hands
    /// a catalog of its own, to show that a theme file added is a theme the CLI offers.
    /// </summary>
    public Func<ThemeCatalog> Themes { get; init; } = ThemeCatalog.Load;

    /// <summary>
    /// The installed WebView2 runtime's version, or null when there is none. A test says there is
    /// none, to show that only the verbs that draw need it (CLI-NF-04).
    /// </summary>
    public Func<string?> WebViewRuntime { get; init; } = RendererFactory.InstalledRuntime;

    /// <summary>
    /// Makes an export run's renderer. Null for the real one; a test hands a fake, to count what a
    /// run starts or to stand in for a browser.
    /// </summary>
    public Func<RenderSetup, IRendererFactory>? Renderers { get; init; }

    /// <summary>The renderer factory for one export run: the test's, or the real one.</summary>
    public IRendererFactory CreateRenderers(RenderSetup setup) =>
        Renderers?.Invoke(setup) ?? new RendererFactory(setup, WebViewRuntime);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// The process's own. Both streams are UTF-8 without a byte order mark, whatever the console's
    /// code page: stdout carries documents (CLI-OUT-06), and a byte order mark in the middle of a
    /// pipe is a character the next program did not ask for.
    /// </summary>
    public static CliEnvironment FromProcess() => new(
        new StreamWriter(Console.OpenStandardOutput(), Utf8NoBom) { AutoFlush = true },
        new StreamWriter(Console.OpenStandardError(), Utf8NoBom) { AutoFlush = true },
        Console.OpenStandardInput,
        !Console.IsErrorRedirected,
        Environment.GetEnvironmentVariable,
        Environment.CurrentDirectory);
}
