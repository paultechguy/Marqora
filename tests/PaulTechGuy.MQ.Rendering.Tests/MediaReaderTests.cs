// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Rendering.Tests;

/// <summary>
/// Pictures written as raw HTML, and where they are.
///
/// The position is the whole point of this file. Everything downstream draws an underline from
/// the line and column reported here, and a mark in the wrong place is worse than no mark: it
/// tells a reader the fault is somewhere it is not. Markdown references get their positions from
/// Markdig, which has kept them accurate for years; these are worked out here from a block's own
/// line offsets, so they are asserted rather than assumed.
/// </summary>
public class MediaReaderTests
{
    private static readonly MarkdigMarkdownRenderer Renderer =
        new(NullLogger<MarkdigMarkdownRenderer>.Instance);

    private static IReadOnlyList<LinkReference> Media(string markdown) =>
        [.. Renderer.Render(markdown).Links.Where(l => l.IsRawHtml)];

    [Fact]
    public void An_html_image_on_its_own_line_is_found_where_it_is_written()
    {
        LinkReference found = Media("""
            Before.

            <img src="shot.png" width="200">

            After.
            """).ShouldHaveSingleItem();

        found.Url.ShouldBe("shot.png");
        found.IsImage.ShouldBeTrue();
        found.IsRawHtml.ShouldBeTrue();
        found.SourceLine.ShouldBe(2);

        // The column is the address itself, not the "<" - see MarkdownMediaReader.
        found.SourceColumn.ShouldBe("""<img src=" """.TrimEnd().Length);
        found.Length.ShouldBe("shot.png".Length);
    }

    [Fact]
    public void An_html_image_in_the_middle_of_a_paragraph_is_found()
    {
        LinkReference found = Media("""
            Here is <img src="inline.png" alt="x"> in a sentence.
            """).ShouldHaveSingleItem();

        found.Url.ShouldBe("inline.png");
        found.SourceLine.ShouldBe(0);
        found.SourceColumn.ShouldBe("""Here is <img src=" """.TrimEnd().Length);
    }

    [Fact]
    public void A_tag_split_over_several_lines_is_left_alone()
    {
        // Every column reported here is an offset into one line of the source. A tag that opens
        // on one line and carries its address on the next cannot be positioned that way - the
        // arithmetic put the underline past the end of the opening line, which is a mark drawn in
        // empty space. Silence is the right failure; see MarkdownMediaReader.
        Media("""
            Before.

            <video
              poster="frame.png">
            </video>
            """).ShouldBeEmpty();
    }

    [Fact]
    public void A_block_of_several_tags_positions_each_one()
    {
        IReadOnlyList<LinkReference> found = Media("""
            Before.

            <img src="one.png">
            <img src="two.png">
            """);

        found.Select(f => f.Url).ShouldBe(["one.png", "two.png"]);
        found.Select(f => f.SourceLine).ShouldBe([2, 3]);
        found.Select(f => f.SourceColumn).ShouldBe([10, 10]);
    }

    [Fact]
    public void An_indented_tag_is_reported_at_its_real_column()
    {
        // Indented markup inside a list item. The column has to count the indent, or the
        // underline lands short of the address by however far the block was pushed in.
        LinkReference found = Media("""
            - A step:

              <img src="step.png">
            """).ShouldHaveSingleItem();

        found.Url.ShouldBe("step.png");
        found.SourceLine.ShouldBe(2);
        found.SourceColumn.ShouldBe("""  <img src=" """.TrimEnd().Length);
    }

    [Fact]
    public void A_tag_carrying_two_addresses_reports_both()
    {
        IReadOnlyList<LinkReference> found = Media("""
            <video src="clip.mp4" poster="frame.png"></video>
            """);

        found.Select(f => f.Url).ShouldBe(["clip.mp4", "frame.png"]);

        // Neither underline may cover the other, or one mark is drawn over the top of the other
        // and only one of them can be right-clicked.
        found[0].SourceColumn.ShouldBeLessThan(found[1].SourceColumn);
        (found[0].SourceColumn + found[0].Length).ShouldBeLessThan(found[1].SourceColumn);
    }

    [Fact]
    public void Every_candidate_in_a_srcset_is_its_own_reference()
    {
        IReadOnlyList<LinkReference> found = Media("""
            <img srcset="small.png 1x, large.png 2x" src="fallback.png">
            """);

        found.Select(f => f.Url).ShouldBe(["small.png", "large.png", "fallback.png"]);

        // The retina one is the interesting case: it is the address nobody looks at, and its
        // offset has to skip the descriptor and the comma before it.
        LinkReference large = found[1];
        large.SourceColumn.ShouldBe("""<img srcset="small.png 1x, """.Length);
        large.Length.ShouldBe("large.png".Length);
    }

    [Theory]
    [InlineData("iframe", "src")]
    [InlineData("audio", "src")]
    [InlineData("track", "src")]
    [InlineData("embed", "src")]
    [InlineData("object", "data")]
    [InlineData("source", "src")]
    public void Every_element_that_fetches_something_is_read(string element, string attribute)
    {
        Media($"""<{element} {attribute}="thing.dat"></{element}>""")
            .ShouldHaveSingleItem()
            .Url.ShouldBe("thing.dat");
    }

    [Fact]
    public void An_anchor_is_not_media()
    {
        // A link is a navigation. Nothing is fetched until somebody clicks, which is the line
        // the whole rule is drawn on.
        Media("""<a href="https://example.com">text</a>""").ShouldBeEmpty();
    }

    [Fact]
    public void Html_inside_a_fence_is_an_example_and_not_a_picture()
    {
        Media("""
            ```html
            <img src="shot.png">
            ```
            """).ShouldBeEmpty();
    }

    [Fact]
    public void Html_inside_a_comment_renders_nothing_and_is_not_reported()
    {
        Media("""
            <!--
            <img src="shot.png">
            -->
            """).ShouldBeEmpty();
    }

    [Fact]
    public void A_markdown_image_is_not_reported_here_twice()
    {
        // The markdown walk already has this one. Reporting it again would draw two marks on one
        // reference, which is the failure this reader is shaped to avoid.
        Media("![alt](shot.png)").ShouldBeEmpty();

        Renderer.Render("![alt](shot.png)").Links.ShouldHaveSingleItem()
            .IsRawHtml.ShouldBeFalse();
    }

    [Fact]
    public void An_attribute_with_no_value_is_not_a_reference()
    {
        Media("""<img src="" alt="nothing">""").ShouldBeEmpty();
    }

    [Fact]
    public void Single_quoted_and_unquoted_attributes_are_read_too()
    {
        Media("<img src='single.png'>").ShouldHaveSingleItem().Url.ShouldBe("single.png");
        Media("<img src=bare.png>").ShouldHaveSingleItem().Url.ShouldBe("bare.png");
    }
}
