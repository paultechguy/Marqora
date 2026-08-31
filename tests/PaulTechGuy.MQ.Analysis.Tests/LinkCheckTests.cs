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

        folder.Check("See [the readme](./README.md).").ShouldBeEmpty();
    }

    [Fact]
    public void A_link_to_a_file_that_is_not_there_is_reported()
    {
        using var folder = new DocumentFolder();

        Diagnostic found = folder.Check("See [the readme](./nope.md).").ShouldHaveSingleItem();

        found.Rule.ShouldBe("broken-link");
        found.Severity.ShouldBe(DiagnosticSeverity.Warning);
        found.Line.ShouldBe(0);
    }

    [Fact]
    public void A_missing_image_is_reported_as_an_image()
    {
        using var folder = new DocumentFolder();

        folder.Check("![a diagram](missing.png)").ShouldHaveSingleItem()
            .Rule.ShouldBe("missing-image");
    }

    [Fact]
    public void An_image_that_is_there_is_not_reported()
    {
        using var folder = new DocumentFolder().With("art/logo.png");

        folder.Check("![logo](art/logo.png)").ShouldBeEmpty();
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
        folder.Check($"[out]({url})").ShouldBeEmpty();
    }

    [Fact]
    public void A_query_or_fragment_is_not_part_of_the_file_name()
    {
        using var folder = new DocumentFolder().With("guide.md");

        folder.Check("[a](guide.md#setup) and [b](guide.md?v=2)").ShouldBeEmpty();
    }

    [Fact]
    public void An_unsaved_document_reports_nothing_about_its_links()
    {
        // There is no folder for "./anything.md" to be relative to, so every link would
        // look broken. Saying nothing beats saying everything.
        DocumentFolder.CheckUnsaved("[nowhere](./anything.md)").ShouldBeEmpty();
    }

    [Fact]
    public void An_anchor_matching_a_heading_is_fine_and_one_that_does_not_is_reported()
    {
        using var folder = new DocumentFolder();

        folder.Check("# Getting Started\n\n[jump](#getting-started)").ShouldBeEmpty();

        folder.Check("# Getting Started\n\n[jump](#getting-stated)").ShouldHaveSingleItem()
            .Rule.ShouldBe("dead-anchor");
    }

    [Fact]
    public void Anchors_are_checked_even_in_an_unsaved_document()
    {
        // Unlike a file path, an anchor can be resolved without knowing where the document
        // lives, so there is no reason to skip it.
        DocumentFolder.CheckUnsaved("# Title\n\n[jump](#nowhere)").ShouldHaveSingleItem()
            .Rule.ShouldBe("dead-anchor");
    }

    [Fact]
    public void A_link_inside_a_fenced_code_block_is_not_a_link()
    {
        using var folder = new DocumentFolder();

        // Never parsed as a link in the first place, so nothing has to filter it out.
        folder.Check("```\n[example](./nope.md)\n```").ShouldBeEmpty();
    }

    [Fact]
    public void A_link_refusing_to_stay_inside_the_document_folder_is_reported()
    {
        using var folder = new DocumentFolder();

        folder.Check("[escape](../../../windows/system32/drivers/etc/hosts)").ShouldHaveSingleItem()
            .Rule.ShouldBe("broken-link");
    }

    [Fact]
    public void An_anchor_written_by_hand_as_html_is_a_real_target()
    {
        // The long-standing way to give something that is not a heading a link target. The
        // preview honours it, so reporting it as dead would be reporting a working link.
        DocumentFolder.CheckUnsaved(
            "<a id=\"notes\"></a>\n\nSome notes.\n\n[jump](#notes)").ShouldBeEmpty();
    }

    [Fact]
    public void An_html_anchor_in_the_middle_of_a_paragraph_counts_too()
    {
        // How a glossary is usually written: the anchor sits inline, immediately before the
        // term it names, rather than on a line of its own.
        DocumentFolder.CheckUnsaved(
            "See [tenant](#g-tenant).\n\n<a id=\"g-tenant\"></a>**Tenant** - one customer.")
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_older_name_attribute_counts_on_an_anchor()
    {
        DocumentFolder.CheckUnsaved("<a name=\"top\"></a>\n\n[back](#top)").ShouldBeEmpty();
    }

    [Fact]
    public void An_id_on_any_element_counts_because_any_element_can_be_a_target()
    {
        DocumentFolder.CheckUnsaved("<div id=\"panel\">text</div>\n\n[jump](#panel)").ShouldBeEmpty();
    }

    [Fact]
    public void An_anchor_only_shown_as_an_example_is_not_a_target()
    {
        // Inside a fence it is sample markup, not markup, and the parser has already said so.
        DocumentFolder.CheckUnsaved("```html\n<a id=\"notes\"></a>\n```\n\n[jump](#notes)")
            .ShouldHaveSingleItem()
            .Rule.ShouldBe("dead-anchor");
    }

    [Fact]
    public void A_name_on_something_that_is_not_an_anchor_is_not_a_target()
    {
        // On an input it names a form field, which is nothing to do with linking.
        DocumentFolder.CheckUnsaved("<input name=\"email\">\n\n[jump](#email)")
            .ShouldHaveSingleItem()
            .Rule.ShouldBe("dead-anchor");
    }

    [Fact]
    public void The_reported_position_is_where_the_link_actually_is()
    {
        using var folder = new DocumentFolder();

        // Proves the line and column come from Markdig's own span rather than a guess.
        Diagnostic found = folder.Check("intro\n\nsee [here](./gone.md) please").ShouldHaveSingleItem();

        found.Line.ShouldBe(2);
        found.Column.ShouldBe(4);
        found.EndColumn.ShouldBeGreaterThan(found.Column);
    }
}
