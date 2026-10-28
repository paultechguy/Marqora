// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Exporting.Tests.Shell;

/// <summary>
/// CLI-EX-06: a diagram leaves as one drawing, whichever way an export takes it. The HTML, PDF and
/// Folio take the rendered markup, which the shell builds through <c>outputMarkup</c>; Word and
/// Copy as SVG ask for the diagram by its hash, which the shell answers through <c>outputSvgOf</c>.
/// Both must hand over the same drawing.
///
/// The headless host shows no screen, so its screen and output drawings are one and the same
/// light one; which of the two a dark window sends is the preview's, and stays with the shell's
/// own code. What is proved here is that the two ways out agree.
/// </summary>
[Collection(nameof(ShellCollection))]
public sealed class DiagramOutputTests(ShellFixture shell)
{
    [Fact]
    public async Task The_rendered_markup_carries_the_drawing_the_svg_request_hands_over()
    {
        string html = await new BridgeSuite(shell).RenderAsync(BridgeSuite.Document);
        string hash = BridgeSuite.HashIn(html);

        string block = Regex.Match(html, $"data-mq-diagram=\"{Regex.Escape(hash)}\"[^>]*>(.*?)</pre>", RegexOptions.Singleline).Groups[1].Value;
        string fromMarkup = Regex.Match(block, "<svg.*</svg>", RegexOptions.Singleline).Value;
        string? fromRequest = await shell.RunAsync(host => host.DiagramSvgAsync(hash));

        fromMarkup.ShouldNotBeEmpty("the rendered markup's drawing");
        fromRequest.ShouldNotBeNull();
        IdOf(fromMarkup).ShouldNotBeEmpty();
        Labels(fromMarkup).ShouldContain("Start");
        Shapes(fromMarkup).ShouldNotBeEmpty();

        // The request writes each element's computed style onto it (svgWithPresentation), so the
        // two differ in their style attributes and nothing else: the same id, labels and shapes.
        IdOf(fromRequest).ShouldBe(IdOf(fromMarkup));
        Labels(fromRequest).ShouldBe(Labels(fromMarkup));
        Shapes(fromRequest).ShouldBe(Shapes(fromMarkup));
    }

    private static string IdOf(string svg) => Regex.Match(svg, "^<svg[^>]*\\bid=\"([^\"]+)\"").Groups[1].Value;

    private static string[] Labels(string svg) =>
        [.. Regex.Matches(svg, ">([^<>]+)<").Select(m => m.Groups[1].Value.Trim()).Where(t => t.Length > 0 && !t.Contains('{', StringComparison.Ordinal))];

    private static string[] Shapes(string svg) =>
        [.. Regex.Matches(svg, "<(path|rect|circle|ellipse|polygon|line)\\b").Select(m => m.Groups[1].Value)];
}
