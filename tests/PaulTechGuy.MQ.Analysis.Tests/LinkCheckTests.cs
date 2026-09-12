// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Analysis.Tests;

public class LinkCheckTests
{
    [Fact]
    public void A_link_to_a_file_that_is_there_is_not_reported()
    {
        using var folder = new DocumentFolder().With("README.md");

        folder.Links("See [the readme](./README.md).").ShouldBeEmpty();
    }

    [Fact]
    public void A_link_to_a_file_that_is_not_there_is_reported()
    {
        using var folder = new DocumentFolder();

        LinkFinding found = folder.Links("See [the readme](./nope.md).").ShouldHaveSingleItem();

        found.Kind.ShouldBe(LinkFindingKind.BrokenLink);
        found.Line.ShouldBe(0);

        // The target travels with the finding, because the menu that offers a replacement has
        // the finding and not the line it came from.
        found.Url.ShouldBe("./nope.md");
        found.Message.ShouldBe("Nothing at \"./nope.md\".");
    }

    [Fact]
    public void A_missing_image_is_reported_as_an_image()
    {
        using var folder = new DocumentFolder();

        folder.Links("![a diagram](missing.png)").ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.MissingImage);
    }

    [Fact]
    public void An_image_that_is_there_is_not_reported()
    {
        using var folder = new DocumentFolder().With("art/logo.png");

        folder.Links("![logo](art/logo.png)").ShouldBeEmpty();
    }

    [Fact]
    public void An_image_in_a_folder_that_merely_starts_with_this_ones_name_is_still_outside_it()
    {
        using var folder = new DocumentFolder();
        string link = folder.WithPrefixedSibling("logo.png");

        // The file really is on disk, and it is still outside the document's folder, which is
        // what the check refuses - the preview cannot serve it either. Comparing the paths as
        // bare strings said otherwise, because "...\abc2\logo.png" starts with "...\abc".
        //
        // Reporting it at all is the containment guarantee: a check that thought this was inside
        // the folder would find the file, be satisfied, and say nothing.
        LinkFinding found = folder.Links($"![logo]({link})").ShouldHaveSingleItem();

        // OutsideFolder rather than MissingImage, and that distinction is the point. This file is
        // not missing - it is one folder over, exactly where the author put it. Saying "no image
        // at ..." sent people looking for something that was never lost.
        found.Kind.ShouldBe(LinkFindingKind.OutsideFolder);
        found.Message.ShouldContain("outside the document's folder");
    }

    [Fact]
    public void A_link_to_the_documents_own_folder_is_not_reported()
    {
        using var folder = new DocumentFolder();

        // "." resolves to the folder itself. Refusing anything outside the folder must not
        // start refusing the folder, which is what a trailing separator on the root alone
        // would do.
        folder.Links("[here](.)").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://example.com/nope.md")]
    [InlineData("http://example.com")]
    [InlineData("mailto:paul@example.com")]
    [InlineData("//example.com/thing")]
    public void Links_that_leave_the_machine_are_left_alone(string url)
    {
        using var folder = new DocumentFolder();

        // Checking these would mean going to the network, which the app never does.
        folder.Links($"[out]({url})").ShouldBeEmpty();
    }

    [Fact]
    public void A_query_or_fragment_is_not_part_of_the_file_name()
    {
        using var folder = new DocumentFolder().With("guide.md");

        folder.Links("[a](guide.md#setup) and [b](guide.md?v=2)").ShouldBeEmpty();
    }

    [Fact]
    public void An_unsaved_document_reports_nothing_about_its_links()
    {
        // There is no folder for "./anything.md" to be relative to, so every link would
        // look broken. Saying nothing beats saying everything.
        DocumentFolder.LinksUnsaved("[nowhere](./anything.md)").ShouldBeEmpty();
    }

    [Fact]
    public void An_anchor_matching_a_heading_is_fine_and_one_that_does_not_is_reported()
    {
        using var folder = new DocumentFolder();

        folder.Links("# Getting Started\n\n[jump](#getting-started)").ShouldBeEmpty();

        folder.Links("# Getting Started\n\n[jump](#getting-stated)").ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.DeadAnchor);
    }

    [Fact]
    public void Anchors_are_checked_even_in_an_unsaved_document()
    {
        // Unlike a file path, an anchor can be resolved without knowing where the document
        // lives, so there is no reason to skip it.
        DocumentFolder.LinksUnsaved("# Title\n\n[jump](#nowhere)").ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.DeadAnchor);
    }

    [Fact]
    public void A_link_inside_a_fenced_code_block_is_not_a_link()
    {
        using var folder = new DocumentFolder();

        // Never parsed as a link in the first place, so nothing has to filter it out.
        folder.Links("```\n[example](./nope.md)\n```").ShouldBeEmpty();
    }

    [Fact]
    public void A_link_refusing_to_stay_inside_the_document_folder_is_reported()
    {
        using var folder = new DocumentFolder();

        folder.Links("[escape](../../../windows/system32/drivers/etc/hosts)").ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.BrokenLink);
    }

    [Fact]
    public void An_anchor_written_by_hand_as_html_is_a_real_target()
    {
        // The long-standing way to give something that is not a heading a link target. The
        // preview honours it, so reporting it as dead would be reporting a working link.
        DocumentFolder.LinksUnsaved(
            "<a id=\"notes\"></a>\n\nSome notes.\n\n[jump](#notes)").ShouldBeEmpty();
    }

    [Fact]
    public void An_html_anchor_in_the_middle_of_a_paragraph_counts_too()
    {
        // How a glossary is usually written: the anchor sits inline, immediately before the
        // term it names, rather than on a line of its own.
        DocumentFolder.LinksUnsaved(
            "See [tenant](#g-tenant).\n\n<a id=\"g-tenant\"></a>**Tenant** - one customer.")
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_older_name_attribute_counts_on_an_anchor()
    {
        DocumentFolder.LinksUnsaved("<a name=\"top\"></a>\n\n[back](#top)").ShouldBeEmpty();
    }

    [Fact]
    public void An_id_on_any_element_counts_because_any_element_can_be_a_target()
    {
        DocumentFolder.LinksUnsaved("<div id=\"panel\">text</div>\n\n[jump](#panel)").ShouldBeEmpty();
    }

    [Fact]
    public void An_anchor_only_shown_as_an_example_is_not_a_target()
    {
        // Inside a fence it is sample markup, not markup, and the parser has already said so.
        DocumentFolder.LinksUnsaved("```html\n<a id=\"notes\"></a>\n```\n\n[jump](#notes)")
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.DeadAnchor);
    }

    [Fact]
    public void A_name_on_something_that_is_not_an_anchor_is_not_a_target()
    {
        // On an input it names a form field, which is nothing to do with linking.
        DocumentFolder.LinksUnsaved("<input name=\"email\">\n\n[jump](#email)")
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(LinkFindingKind.DeadAnchor);
    }

    [Fact]
    public void The_reported_position_is_where_the_link_actually_is()
    {
        using var folder = new DocumentFolder();

        // Proves the line and column come from Markdig's own span rather than a guess.
        LinkFinding found = folder.Links("intro\n\nsee [here](./gone.md) please").ShouldHaveSingleItem();

        found.Line.ShouldBe(2);
        found.Start.ShouldBe(4);

        // The whole reference is underlined, not just the target, so the squiggle covers what
        // the menu will replace.
        found.Length.ShouldBe("[here](./gone.md)".Length);
    }
}
