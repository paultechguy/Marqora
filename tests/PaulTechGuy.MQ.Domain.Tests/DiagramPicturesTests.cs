// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// A rendered diagram traded for the picture of it, on the way to a clipboard whose destination
/// cannot draw an SVG and will not leave one alone either.
/// </summary>
public sealed class DiagramPicturesTests
{
    /// <summary>
    /// The preview's own shape: the hash stamped on the pre while the definition was still
    /// there, and the SVG mermaid put in the definition's place.
    /// </summary>
    private const string Diagram = """
        <p>Before.</p>
        <pre class="mermaid" data-mq-diagram="1189641" data-mq-index="1"><svg width="400" height="200">
        <text>Source pane</text><text>Preview pane</text></svg></pre>
        <p>After.</p>
        """;

    /// <summary>A four-by-two PNG header, which is all TryReadPngSize reads.</summary>
    private static byte[] Png(int width, int height) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
        (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
        (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
        8, 6, 0, 0, 0,
    ];

    [Fact]
    public void The_hash_is_found_for_fetching() =>
        DiagramPictures.HashesIn(Diagram).ShouldBe(["1189641"]);

    [Fact]
    public void Markup_with_no_diagram_asks_for_nothing() =>
        DiagramPictures.HashesIn("<p>Just a paragraph.</p>").ShouldBeEmpty();

    /// <summary>
    /// The whole point: no SVG survives, so there are no stray text nodes to be mistaken for
    /// the author's own prose.
    /// </summary>
    [Fact]
    public void The_svg_and_its_labels_are_gone()
    {
        string substituted = DiagramPictures.Substitute(
            Diagram, new Dictionary<string, byte[]> { ["1189641"] = Png(800, 400) });

        substituted.ShouldNotContain("<svg");
        substituted.ShouldNotContain("Source pane");
        substituted.ShouldNotContain("Preview pane");
        substituted.ShouldContain("data:image/png;base64,");
    }

    /// <summary>
    /// The shell rasterizes at twice the size, so the picture is stated at half what the file
    /// measures or every diagram pastes into Word at double size.
    /// </summary>
    [Fact]
    public void The_picture_is_stated_at_the_size_it_was_drawn()
    {
        string substituted = DiagramPictures.Substitute(
            Diagram, new Dictionary<string, byte[]> { ["1189641"] = Png(800, 400) });

        substituted.ShouldContain("width=\"400\"");
        substituted.ShouldContain("height=\"200\"");
    }

    /// <summary>Text either side is untouched; only the diagram is replaced.</summary>
    [Fact]
    public void The_surrounding_document_is_left_alone()
    {
        string substituted = DiagramPictures.Substitute(
            Diagram, new Dictionary<string, byte[]> { ["1189641"] = Png(800, 400) });

        substituted.ShouldContain("<p>Before.</p>");
        substituted.ShouldContain("<p>After.</p>");
    }

    /// <summary>
    /// No picture means no diagram, and nothing said about it. A note explaining that Marqora
    /// could not bring the diagram is this app talking in somebody else's document.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_diagram_with_no_picture_is_removed_without_comment(int kind)
    {
        Dictionary<string, byte[]> pictures = kind == 0
            ? []
            : new() { ["1189641"] = [] };

        string substituted = DiagramPictures.Substitute(Diagram, pictures);

        substituted.ShouldNotContain("<svg");
        substituted.ShouldNotContain("<img");
        substituted.ShouldNotContain("iagram");
        substituted.ShouldContain("<p>Before.</p>");
        substituted.ShouldContain("<p>After.</p>");
    }

    /// <summary>
    /// Something that is not a PNG still becomes a picture - the bytes are whatever the shell
    /// sent - but without a size claimed for it, rather than a size invented.
    /// </summary>
    [Fact]
    public void Bytes_with_no_readable_header_carry_no_size()
    {
        string substituted = DiagramPictures.Substitute(
            Diagram, new Dictionary<string, byte[]> { ["1189641"] = [1, 2, 3, 4] });

        substituted.ShouldContain("<img");
        substituted.ShouldNotContain("width=");
        substituted.ShouldNotContain("height=");
    }

    [Fact]
    public void Several_diagrams_each_take_their_own_picture()
    {
        const string two = """
            <pre class="mermaid" data-mq-diagram="aaa"><svg><text>one</text></svg></pre>
            <pre class="mermaid" data-mq-diagram="bbb"><svg><text>two</text></svg></pre>
            """;

        string substituted = DiagramPictures.Substitute(
            two,
            new Dictionary<string, byte[]> { ["aaa"] = Png(200, 100), ["bbb"] = Png(600, 300) });

        substituted.ShouldContain("width=\"100\"");
        substituted.ShouldContain("width=\"300\"");
        substituted.ShouldNotContain("<text>");
    }

    /// <summary>One of two missing takes only itself out.</summary>
    [Fact]
    public void A_missing_picture_does_not_take_its_neighbour_with_it()
    {
        const string two = """
            <pre class="mermaid" data-mq-diagram="aaa"><svg><text>one</text></svg></pre>
            <pre class="mermaid" data-mq-diagram="bbb"><svg><text>two</text></svg></pre>
            """;

        string substituted = DiagramPictures.Substitute(
            two, new Dictionary<string, byte[]> { ["bbb"] = Png(600, 300) });

        substituted.ShouldContain("width=\"300\"");
        substituted.ShouldNotContain("one");
        substituted.ShouldNotContain("two");
    }

    /// <summary>An ordinary code fence is not a diagram and keeps its text.</summary>
    [Fact]
    public void A_code_block_is_not_touched()
    {
        const string code = "<pre><code class=\"language-csharp\">var x = 1;</code></pre>";

        DiagramPictures.Substitute(code, new Dictionary<string, byte[]>()).ShouldBe(code);
    }

    [Fact]
    public void Empty_markup_is_returned_as_it_came() =>
        DiagramPictures.Substitute(string.Empty, new Dictionary<string, byte[]>()).ShouldBe(string.Empty);
}
