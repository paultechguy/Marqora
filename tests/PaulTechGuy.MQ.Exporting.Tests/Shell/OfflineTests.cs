// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Rendering;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Exporting.Tests.Shell;

/// <summary>
/// CLI-NF-03 for the exports: the browser that draws them asks for nothing but Marqora's own files
/// and the document's. The host counts every other request its page makes; the documents here name
/// pictures on the web, embed a frame from one and link everywhere, and the count stays at zero.
///
/// The shell's content policy is what stops those requests, as it does in the preview, so nothing
/// a document says can make one: the count cannot be driven above zero from here, only shown to
/// stay there with the documents most likely to move it.
/// </summary>
[Collection(nameof(ShellCollection))]
public sealed class OfflineTests(ShellFixture shell)
{
    [Theory]
    [InlineData("docs/UltimateMarkdownContent.md")]
    [InlineData("webshell/cheatsheet.md")]
    [InlineData("tests/baselines/cli/inputs/pictures.md")]
    public async Task Rendering_a_document_asks_for_nothing_outside_marqora(string relative)
    {
        string path = Path.Combine(RepoRoot(), relative);
        string text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        int before = await shell.RunAsync(host => Task.FromResult(host.OutsideRequests));

        BridgeOutcome<RenderedDocument> rendered = await shell.RunAsync(host =>
            host.RenderAsync(new RenderRequest(text, path, HeadingNumbering.Off, shell.Themes.Default.Id)));

        // The Folio's way in too: the same document finished detached, beside the page.
        string markup = new MarkdigMarkdownRenderer(NullLogger<MarkdigMarkdownRenderer>.Instance).Render(text, HeadingNumbering.Off).Html;
        BridgeOutcome<string> detached = await shell.RunAsync(host => host.RenderDetachedAsync(markup));

        rendered.IsRendered.ShouldBeTrue(rendered.Error?.Message);
        detached.IsRendered.ShouldBeTrue(detached.Error?.Message);
        (await shell.RunAsync(host => Task.FromResult(host.OutsideRequests))).ShouldBe(before);
    }

    /// <summary>The folder holding the solution, found by walking up from the tests' output.</summary>
    private static string RepoRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (folder.EnumerateFiles("*.sln*").Any())
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above the test output.");
    }
}
