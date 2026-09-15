// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Markdown.Tests;

public class LinkTargetScannerTests
{
    private static IReadOnlyList<LinkTargetScanner.LinkTarget> Find(string document) =>
        LinkTargetScanner.Find(document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));

    private static IReadOnlyList<string> Anchors(string document) =>
        [.. Find(document).Select(t => t.Anchor)];

    [Fact]
    public void An_inline_destination_is_found()
    {
        Anchors("See [the scope](#scope) for more.").ShouldBe(["scope"]);
    }

    [Fact]
    public void A_title_after_the_destination_is_not_part_of_it()
    {
        Anchors("[x](#scope \"The scope\")").ShouldBe(["scope"]);
    }

    [Fact]
    public void Angle_brackets_around_a_destination_are_not_part_of_it()
    {
        Anchors("[x](<#scope>)").ShouldBe(["scope"]);
    }

    [Fact]
    public void A_reference_definition_is_found()
    {
        Anchors("[ref]: #scope").ShouldBe(["scope"]);
        Anchors("   [ref]: #scope \"Title\"").ShouldBe(["scope"]);
    }

    [Fact]
    public void A_raw_html_href_is_found()
    {
        // A table of contents written as real anchors is as ordinary as one written in
        // markdown, and its links break the same way.
        Anchors("<a href=\"#scope\">Scope</a>").ShouldBe(["scope"]);
        Anchors("<a href='#scope'>Scope</a>").ShouldBe(["scope"]);
        Anchors("<a href=#scope>Scope</a>").ShouldBe(["scope"]);
    }

    [Fact]
    public void An_image_destination_counts_too()
    {
        Anchors("![alt](#scope)").ShouldBe(["scope"]);
    }

    [Fact]
    public void A_link_into_another_document_is_left_alone()
    {
        // This can only speak for anchors in the file it was given.
        Find("[x](other.md#scope)").ShouldBeEmpty();
        Find("[x](https://example.com/page#scope)").ShouldBeEmpty();
    }

    [Fact]
    public void A_bare_hash_names_no_heading()
    {
        Find("[back to top](#)").ShouldBeEmpty();
    }

    [Fact]
    public void A_destination_inside_a_code_span_is_not_a_link()
    {
        // The worked example in a document about writing links must not be rewritten when the
        // headings around it move.
        Find("Write it as `[text](#scope)` in your source.").ShouldBeEmpty();
    }

    [Fact]
    public void A_destination_inside_a_fence_is_not_a_link()
    {
        Find(
            """
            ```markdown
            [x](#scope)
            ```
            """).ShouldBeEmpty();
    }

    [Fact]
    public void The_column_points_past_the_hash_so_only_the_name_is_replaced()
    {
        const string line = "See [the scope](#scope) for more.";

        LinkTargetScanner.LinkTarget target = Find(line)[0];

        line[target.Column..target.End].ShouldBe("scope");
        line[target.Column - 1].ShouldBe('#');

        string rebuilt = string.Concat(line.AsSpan(0, target.Column), "2--scope", line.AsSpan(target.End));

        rebuilt.ShouldBe("See [the scope](#2--scope) for more.");
    }

    [Fact]
    public void Several_links_on_one_line_come_back_in_order()
    {
        Anchors("[a](#one) and [b](#two) and [c](#three)").ShouldBe(["one", "two", "three"]);
    }

    [Fact]
    public void A_table_of_contents_is_found_line_by_line()
    {
        IReadOnlyList<LinkTargetScanner.LinkTarget> targets = Find(
            """
            # Contents

            - [Introduction](#introduction)
            - [Scope](#scope)

            # Introduction
            """);

        targets.Select(t => t.Anchor).ShouldBe(["introduction", "scope"]);
        targets.Select(t => t.Line).ShouldBe([2, 3]);
    }
}
