// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.Cli.Rendering;

/// <summary>
/// Where an export run's renderer comes from (CLI-EX-01): made when the run's first export needs
/// it, used for every file after, and ended with the run. A run of text verbs never asks for one,
/// and a test hands a run a factory of its own to show it.
/// </summary>
internal interface IRendererFactory : IAsyncDisposable
{
    /// <summary>
    /// Runs <paramref name="work"/> against the run's renderer, starting it the first time, on the
    /// thread the renderer belongs to.
    /// </summary>
    /// <exception cref="RendererUnavailableException">There is no renderer to be had: WebView2 is missing, or would not start.</exception>
    Task<T> RunAsync<T>(Func<IDocumentRenderer, Task<T>> work, CancellationToken cancellationToken);
}

/// <summary>What a run's renderer is started with.</summary>
/// <param name="InstallPaths">Where the shell's files are: beside marqora-cli.exe.</param>
/// <param name="ThemeId">The color theme the run's outputs wear.</param>
/// <param name="Preferences">The preview preferences of the settings source, which change how diagrams are drawn.</param>
/// <param name="Timeout">How long one document's render may take (CLI-EX-05).</param>
internal sealed record RenderSetup(
    IAppPaths InstallPaths,
    ThemeCatalog Themes,
    string ThemeId,
    PreviewPreferences Preferences,
    TimeSpan Timeout,
    Reporter Reporter);

/// <summary>No renderer can be had, for any file of the run: exit 5, with the sentence that says what to do.</summary>
internal sealed class RendererUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One document's render did not finish, or failed: that file exits 5 and the run moves on (CLI-EX-05).</summary>
internal sealed class RenderFailedException(string message, Exception? inner = null) : Exception(message, inner);
