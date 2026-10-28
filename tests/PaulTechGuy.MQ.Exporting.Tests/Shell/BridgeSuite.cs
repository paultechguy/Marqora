// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Exporting.Tests.Shell;

/// <summary>
/// CLI-ARC-05 with amendment A12: one suite over the bridge and transport both hosts speak to the
/// shell through - <c>ShellBridge</c> over <c>CoreWebView2Transport</c> - with a real shell at the
/// other end. The live preview uses the same two over its own control, which a test process
/// cannot host; the headless host uses them over its controller, which is what runs here. Each
/// request the exports make goes out and comes back with its answer.
/// </summary>
[Collection(nameof(ShellCollection))]
public sealed class BridgeSuite(ShellFixture shell)
{
    /// <summary>A diagram, an equation and some code: everything the exports ask the shell to finish.</summary>
    internal const string Document = "# Bridge\n\n```mermaid\nflowchart LR\n  A[Start] --> B[End]\n```\n\n$$\nx^2 + 1\n$$\n\n```csharp\nreturn 42;\n```\n";

    /// <summary>The line the equation's block starts on, counted from zero as Markdig counts.</summary>
    private const int MathLine = 7;

    [Fact]
    public async Task A_render_comes_back_finished_with_its_diagram_math_and_highlighting()
    {
        string html = await RenderAsync(Document);

        html.ShouldContain("<svg", Case.Sensitive, "the diagram, drawn");
        html.ShouldContain("class=\"katex", Case.Sensitive, "the equation, typeset");
        html.ShouldContain("hljs-keyword", Case.Sensitive, "the code, highlighted");
    }

    [Fact]
    public async Task A_diagram_asked_for_by_its_hash_comes_back_as_svg_and_as_png()
    {
        string hash = HashIn(await RenderAsync(Document));

        string? svg = await shell.RunAsync(host => host.DiagramSvgAsync(hash));
        byte[]? png = await shell.RunAsync(host => host.DiagramPngAsync(hash));

        svg.ShouldNotBeNull().ShouldStartWith("<svg");
        png.ShouldNotBeNull();
        png[..4].ShouldBe(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' });
    }

    [Fact]
    public async Task An_equation_asked_for_by_its_line_comes_back_as_a_picture()
    {
        await RenderAsync(Document);

        MathPicture? picture = await shell.RunAsync(host => host.MathPngAsync(MathLine, 0));

        picture.ShouldNotBeNull();
        picture.Png[..4].ShouldBe(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' });
    }

    [Fact]
    public async Task A_hash_the_page_does_not_hold_is_answered_with_nothing_rather_than_left_waiting()
    {
        await RenderAsync(Document);

        (await shell.RunAsync(host => host.DiagramSvgAsync("no-such-diagram"))).ShouldBeNull();
    }

    [Fact]
    public async Task Markup_finished_detached_comes_back_drawn_and_leaves_the_open_document_alone()
    {
        string before = await RenderAsync(Document);
        string markup = "<h1>Elsewhere</h1>\n<pre class=\"mermaid\">flowchart TD\n  X --> Y\n</pre>\n";

        BridgeOutcome<string> finished = await shell.RunAsync(host => host.RenderDetachedAsync(markup));

        finished.IsRendered.ShouldBeTrue(finished.Error?.Message);
        finished.Value.ShouldNotBeNull().ShouldContain("<svg");

        // The open document's diagram is still there to be asked for: a Folio's documents are
        // drawn beside the page, never over it.
        (await shell.RunAsync(host => host.DiagramSvgAsync(HashIn(before)))).ShouldNotBeNull();
    }

    /// <summary>Renders <paramref name="markdown"/> as the shell's one tab and answers with the finished markup.</summary>
    internal async Task<string> RenderAsync(string markdown, string? path = null, string? themeId = null)
    {
        BridgeOutcome<RenderedDocument> outcome = await shell.RunAsync(host =>
            host.RenderAsync(new RenderRequest(markdown, path, HeadingNumbering.Off, themeId ?? shell.Themes.Default.Id)));

        outcome.IsRendered.ShouldBeTrue(outcome.Error?.Message);

        return outcome.Value.ShouldNotBeNull().Html;
    }

    /// <summary>The first diagram's hash in some rendered markup.</summary>
    internal static string HashIn(string html) =>
        Regex.Match(html, "data-mq-diagram=\"([^\"]+)\"") is { Success: true } match
            ? match.Groups[1].Value
            : throw new ShouldAssertException("The rendered markup has no diagram.");
}
