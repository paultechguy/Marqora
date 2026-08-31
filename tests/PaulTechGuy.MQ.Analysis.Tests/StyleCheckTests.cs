// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Analysis.Tests;

public class StyleCheckTests
{
    [Fact]
    public void A_heading_with_no_space_after_the_hashes_is_reported()
    {
        Rules("##Title").ShouldBe(["heading-space"]);
    }

    [Fact]
    public void A_list_marker_with_no_space_after_it_is_reported()
    {
        Rules("-item").ShouldBe(["list-marker-space"]);
    }

    [Theory]
    [InlineData("*A summary of the thing.*")]
    [InlineData("*Glossary: [Tenant](https://example.com)*")]
    [InlineData("*emphasis* then more words")]
    public void Emphasis_at_the_start_of_a_line_is_not_a_bullet_missing_its_space(string line)
    {
        // Written identically to "*item", and a renderer reads it as emphasis. The formatter
        // leaves it alone, so squiggling it would point at something nothing would fix.
        Rules(line).ShouldBeEmpty();
    }

    [Fact]
    public void An_asterisk_with_no_partner_really_is_a_bullet_missing_its_space()
    {
        Rules("*item").ShouldBe(["list-marker-space"]);
    }

    [Fact]
    public void A_blockquote_marker_with_no_space_after_it_is_reported()
    {
        Rules(">quoted").ShouldBe(["blockquote-space"]);
    }

    [Fact]
    public void Trailing_whitespace_is_reported()
    {
        Rules("Some text   ").ShouldBe(["trailing-whitespace"]);
    }

    [Fact]
    public void A_space_between_link_text_and_target_is_reported()
    {
        Rules("[text] (https://example.com)").ShouldBe(["link-syntax"]);
    }

    [Fact]
    public void Style_rules_are_hints_rather_than_warnings()
    {
        // The formatter fixes all of these on request, so shouting about them would be
        // making noise about something already solved.
        DocumentFolder.CheckUnsaved("##Title").ShouldHaveSingleItem()
            .Severity.ShouldBe(DiagnosticSeverity.Hint);
    }

    [Fact]
    public void Tidy_markdown_reports_nothing()
    {
        Rules("# Title\n\n- one\n- two\n\n> quoted\n").ShouldBeEmpty();
    }

    [Fact]
    public void Nothing_inside_a_fenced_code_block_is_reported()
    {
        // Trailing spaces and hash-runs are content in there, not mistakes.
        Rules("```\n##NotAHeading   \n-notalist\n```").ShouldBeEmpty();
    }

    [Fact]
    public void A_tilde_fence_protects_its_contents_too()
    {
        Rules("~~~\n##NotAHeading\n~~~").ShouldBeEmpty();
    }

    [Fact]
    public void Nothing_inside_front_matter_is_reported()
    {
        Rules("---\ntitle:   Something   \n---\n\n# Real\n").ShouldBeEmpty();
    }

    [Fact]
    public void Front_matter_only_counts_at_the_very_top()
    {
        // A rule partway down a document is a thematic break, and what follows it is
        // ordinary content that should still be checked.
        Rules("# Title\n\n---\n\n##Heading").ShouldBe(["heading-space"]);
    }

    [Fact]
    public void Bad_syntax_inside_a_code_span_is_an_example_rather_than_a_mistake()
    {
        Rules("Write `[text] (url)` to see the problem.").ShouldBeEmpty();
    }

    [Fact]
    public void A_carriage_return_is_not_trailing_whitespace()
    {
        // Splitting on \n alone leaves one on every line of a CRLF file. Reporting those
        // would mark up the whole document.
        Rules("# Title\r\n\r\nSome text\r\n").ShouldBeEmpty();
    }

    [Fact]
    public void The_reported_column_points_at_the_offending_characters()
    {
        Diagnostic found = DocumentFolder.CheckUnsaved("Some text   ").ShouldHaveSingleItem();

        found.Line.ShouldBe(0);
        found.Column.ShouldBe(9);
        found.EndColumn.ShouldBe(12);
    }

    /// <summary>The rules that fired, which is what these tests are really asserting on.</summary>
    private static string[] Rules(string markdown) =>
        [.. DocumentFolder.CheckUnsaved(markdown).Select(d => d.Rule)];
}
