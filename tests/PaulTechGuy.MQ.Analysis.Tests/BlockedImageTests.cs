// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Analysis.Tests;

/// <summary>
/// Pictures that will not appear, and the reason each one will not.
///
/// The rule these test is narrow on purpose: a reference is reported when rendering the document
/// would need a fetch this app refuses to make, or a file it refuses to serve. Loads, not
/// navigations - which is why half of this file is about what must stay silent. A README carrying
/// a dozen ordinary links that lit up would be a rule switched off within the day.
/// </summary>
public class BlockedImageTests
{
    private static LinkFinding? Blocked(IReadOnlyList<LinkFinding> findings) =>
        findings.FirstOrDefault(f =>
            f.Kind is LinkFindingKind.RemoteMedia or LinkFindingKind.OutsideFolder);

    // ---- on the web -------------------------------------------------------

    [Theory]
    [InlineData("https://example.com/shot.png")]
    [InlineData("http://example.com/shot.png")]
    [InlineData("//example.com/shot.png")]
    [InlineData("HTTPS://EXAMPLE.COM/SHOT.PNG")]
    public void An_image_addressed_on_the_web_is_reported(string url)
    {
        using var folder = new DocumentFolder();

        folder.Links($"![shot]({url})").ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.RemoteMedia);
    }

    [Fact]
    public void The_message_says_the_document_is_not_at_fault()
    {
        using var folder = new DocumentFolder();

        LinkFinding found = folder.Links("![badge](https://img.shields.io/badge/build-passing.svg)")
            .ShouldHaveSingleItem();

        // The second sentence is the one that matters. Without it, eight badges read as eight
        // accusations about a document that renders perfectly well for everybody who will
        // actually see it.
        found.Message.ShouldBe(
            "Marqora does not load content from the web, so this will not appear in the preview. "
            + "It will still work anywhere that does.");

        // The address is on the line the pointer is resting on, so the hover does not repeat it.
        found.Message.ShouldNotContain("img.shields.io");
    }

    [Theory]
    [InlineData("![shot](https://example.com/shot.png)")]
    [InlineData(@"![shot](C:\Users\paul\Pictures\shot.png)")]
    public void Every_message_ends_in_a_period(string markdown)
    {
        using var folder = new DocumentFolder();

        folder.Links(markdown).ShouldHaveSingleItem()
            .Message.ShouldEndWith(".");
    }

    [Theory]
    [InlineData("[out](https://example.com/page)")]
    [InlineData("<https://example.com/page>")]
    [InlineData("[out](//example.com/page)")]
    [InlineData("[mail](mailto:paul@example.com)")]
    public void A_link_is_a_navigation_and_is_never_reported(string markdown)
    {
        using var folder = new DocumentFolder();

        // This is the line the whole rule is drawn on. Nothing is fetched until somebody clicks,
        // and clicking works - the host hands it to the browser.
        Blocked(folder.Links(markdown)).ShouldBeNull();
    }

    [Fact]
    public void A_badge_is_reported_like_any_other_picture()
    {
        using var folder = new DocumentFolder();

        // The alt-text rule skips a badge, because the link around it supplies the accessible
        // name. That reasoning says nothing about whether the picture appears, and it does not:
        // it is a blank box like any other, and the one thing nothing else would explain.
        folder.Links("[![build](https://ci.example/badge.svg)](https://ci.example)")
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.RemoteMedia);
    }

    [Fact]
    public void A_data_uri_renders_and_is_left_alone()
    {
        using var folder = new DocumentFolder();

        Blocked(folder.Links("![dot](data:image/png;base64,iVBORw0KGgo=)")).ShouldBeNull();
    }

    [Fact]
    public void A_remote_image_is_reported_even_before_the_document_is_saved()
    {
        // Nothing here needs a folder: no disk is touched to know the app will not go to the web.
        // The dead-link checks give up on an unsaved document and this one must not.
        DocumentFolder.LinksUnsaved("![shot](https://example.com/shot.png)")
            .ShouldContain(f => f.Kind == LinkFindingKind.RemoteMedia);
    }

    // ---- somewhere else on this machine -----------------------------------

    [Fact]
    public void An_image_named_by_absolute_path_that_exists_is_reported_as_outside_the_folder()
    {
        using var folder = new DocumentFolder();
        string absolute = folder.WithAbsoluteSibling("shot.png");

        LinkFinding found = folder.Links($"![shot]({absolute.Replace('\\', '/')})")
            .ShouldHaveSingleItem();

        found.Kind.ShouldBe(LinkFindingKind.OutsideFolder);
        found.Message.ShouldContain("outside the document's folder");
    }

    [Fact]
    public void An_absolute_path_with_nothing_behind_it_is_still_a_missing_image()
    {
        using var folder = new DocumentFolder();

        // The distinction is the point of the pair: this one really is missing, and saying so is
        // correct. The one above is not, and saying so was the bug.
        folder.Links(@"![shot](C:/definitely/not/here/shot.png)").ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.MissingImage);
    }

    [Fact]
    public void A_drive_letter_is_not_mistaken_for_a_url_scheme()
    {
        using var folder = new DocumentFolder();

        // "C:" matched the old scheme test - one letter, then a colon - so a picture named this
        // way was waved through as somebody else's URL and left blank with nothing said about it.
        folder.Links(@"![shot](C:\Users\paul\Pictures\shot.png)").ShouldNotBeEmpty();
    }

    [Fact]
    public void A_file_url_is_reported_too()
    {
        using var folder = new DocumentFolder();
        string absolute = folder.WithAbsoluteSibling("shot.png");

        folder.Links($"![shot](file:///{absolute.Replace('\\', '/')})").ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.OutsideFolder);
    }

    [Fact]
    public void An_absolute_path_inside_the_documents_own_folder_is_fine()
    {
        using var folder = new DocumentFolder();
        folder.With("art/logo.png");

        // Absolutely spelled, but it lands exactly where the preview can serve it from. Nothing
        // is wrong with it beyond the spelling, and the app does not lint spelling of paths.
        Blocked(folder.Links($"![logo]({folder.PathTo("art/logo.png").Replace('\\', '/')})"))
            .ShouldBeNull();
    }

    [Fact]
    public void A_relative_path_climbing_out_to_a_real_file_says_so_rather_than_calling_it_missing()
    {
        using var folder = new DocumentFolder();
        string climbing = folder.WithPrefixedSibling("logo.png");

        LinkFinding found = folder.Links($"![logo]({climbing})").ShouldHaveSingleItem();

        found.Kind.ShouldBe(LinkFindingKind.OutsideFolder);
    }

    [Fact]
    public void A_relative_path_climbing_out_to_nothing_is_still_missing()
    {
        using var folder = new DocumentFolder();

        folder.Links("![logo](../nowhere/logo.png)").ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.MissingImage);
    }

    [Fact]
    public void A_link_climbing_out_of_the_folder_keeps_the_answer_it_always_gave()
    {
        using var folder = new DocumentFolder();
        string climbing = folder.WithPrefixedSibling("notes.md");

        // Links are out of scope for this rule, and that has to include the ones that would have
        // qualified. Changing what a link says was never part of the bargain.
        folder.Links($"[notes]({climbing})").ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.BrokenLink);
    }

    // ---- pictures written as HTML -----------------------------------------

    [Fact]
    public void A_remote_image_written_as_html_is_reported()
    {
        using var folder = new DocumentFolder();

        folder.Links("""<img src="https://example.com/shot.png" width="200">""")
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.RemoteMedia);
    }

    [Fact]
    public void A_remote_iframe_is_reported()
    {
        using var folder = new DocumentFolder();

        folder.Links("""<iframe src="https://example.com/embed" title="demo"></iframe>""")
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.RemoteMedia);
    }

    [Fact]
    public void An_html_anchor_is_not_media()
    {
        using var folder = new DocumentFolder();

        Blocked(folder.Links("""<a href="https://example.com">text</a>""")).ShouldBeNull();
    }

    [Fact]
    public void Html_inside_a_fence_is_an_example_and_says_nothing()
    {
        using var folder = new DocumentFolder();

        Blocked(folder.Links("""
            ```html
            <img src="https://example.com/shot.png">
            ```
            """)).ShouldBeNull();
    }

    [Fact]
    public void An_html_image_is_never_asked_for_alt_text()
    {
        using var folder = new DocumentFolder();

        // Nothing reads an "alt" attribute, so every raw tag would look like an image nobody had
        // described - a fresh mark on documents their authors had already put right.
        folder.Links("""<img src="art/logo.png">""")
            .ShouldNotContain(f => f.Kind == LinkFindingKind.MissingAltText);
    }

    [Fact]
    public void A_markdown_image_still_is_asked_for_alt_text()
    {
        using var folder = new DocumentFolder();
        folder.With("logo.png");

        folder.Links("![](logo.png)").ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.MissingAltText);
    }

    // ---- the switch -------------------------------------------------------

    [Fact]
    public void Turning_the_rule_off_silences_only_these()
    {
        using var folder = new DocumentFolder();

        IReadOnlyList<LinkFinding> found = folder.Links(
            """
            ![shot](https://example.com/shot.png)

            [gone](./nope.md)

            [nowhere](#no-such-heading)
            """,
            blocked: false);

        // The whole reason this has a switch of its own: quieting a badge-heavy README must not
        // cost the dead links and the broken anchors.
        Blocked(found).ShouldBeNull();
        found.Select(f => f.Kind).ShouldBe([LinkFindingKind.BrokenLink, LinkFindingKind.DeadAnchor]);
    }

    // ---- position ---------------------------------------------------------

    [Fact]
    public void The_underline_covers_the_whole_markdown_reference()
    {
        using var folder = new DocumentFolder();

        LinkFinding found = folder.Links("See ![shot](https://example.com/shot.png) here.")
            .ShouldHaveSingleItem();

        found.Line.ShouldBe(0);
        found.Start.ShouldBe("See ".Length);
        found.Length.ShouldBe("![shot](https://example.com/shot.png)".Length);
    }

    [Fact]
    public void The_underline_on_an_html_tag_covers_the_address_alone()
    {
        using var folder = new DocumentFolder();

        // A tag can carry two addresses, and one underline over the whole tag would be two marks
        // drawn on top of each other. Marking the value also lets a repair replace exactly what
        // it underlined, which is what an attribute has instead of "](url)" syntax.
        LinkFinding found = folder.Links("""<img src="https://example.com/shot.png">""")
            .ShouldHaveSingleItem();

        found.Start.ShouldBe("""<img src=" """.TrimEnd().Length);
        found.Length.ShouldBe("https://example.com/shot.png".Length);
    }
}
