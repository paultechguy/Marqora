// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Rendering.Tests;

/// <summary>
/// What numbering a document is and is not allowed to change.
///
/// The first test here is the reason the rest exist. The numbers are written into the parsed
/// document between the parse and the render, which puts them a hair away from the anchor
/// ids: every "#some-heading" link in every document a user has ever written depends on
/// UseAutoIdentifiers having already decided what a heading is called. If that decision were
/// made later - at render time, from the inlines as they then stand - switching a preference
/// on would silently rename every section in the document and break every link into it.
/// </summary>
public partial class HeadingNumberingTests
{
    private static readonly MarkdigMarkdownRenderer Renderer =
        new(NullLogger<MarkdigMarkdownRenderer>.Instance);

    /// <summary>
    /// Opens with prose, skips a level at the end, and gives one heading the punctuation and
    /// inline markup that an auto-generated id has to survive.
    /// </summary>
    private const string Document = """
        Some prose before anything is numbered.

        # Introduction

        ## Why it exists

        ### A note on scope

        ## What it is not

        # Reference & `code` in a heading

        ### Skipped a level
        """;

    // Stops at the closing quote rather than matching it, which keeps a raw string literal
    // out of the business of ending in one.
    [GeneratedRegex("""id="([^"]*)""")]
    private static partial Regex IdAttribute { get; }

    private static IReadOnlyList<string> IdsIn(string html) =>
        [.. IdAttribute.Matches(html).Select(match => match.Groups[1].Value)];

    // ------------------------------------------------------------------ anchors

    /// <summary>
    /// The one that decides whether the whole approach holds.
    /// </summary>
    [Theory]
    [InlineData(HeadingNumbering.FromHeading1)]
    [InlineData(HeadingNumbering.FromHeading2)]
    [InlineData(HeadingNumbering.FromHeading3)]
    public void Numbering_does_not_change_a_heading_anchor(HeadingNumbering numbering)
    {
        IReadOnlyList<string> plain = IdsIn(Renderer.Render(Document, HeadingNumbering.Off).Html);
        IReadOnlyList<string> numbered = IdsIn(Renderer.Render(Document, numbering).Html);

        // Guards against the comparison passing because neither document had any ids at all.
        plain.ShouldContain("introduction");
        numbered.ShouldBe(plain);

        // Said a second way, because the first would still pass if numbering had somehow
        // renamed both renders alike: a leaked number reads as "1-introduction", and no
        // heading in the fixture has a digit in its own words.
        numbered.ShouldAllBe(id => !id.Any(char.IsDigit));
    }

    /// <summary>
    /// The slug the outline carries is the same id, and the analyzer checks in-document
    /// links against it, so it has to move in step with the HTML rather than merely near it.
    /// </summary>
    [Fact]
    public void Numbering_does_not_change_an_outline_slug()
    {
        string[] plain =
            [.. Renderer.Render(Document, HeadingNumbering.Off).Outline.Select(h => h.Slug)];

        string[] numbered =
            [.. Renderer.Render(Document, HeadingNumbering.FromHeading1).Outline.Select(h => h.Slug)];

        numbered.ShouldBe(plain);
    }

    // --------------------------------------------------------------------- html

    [Fact]
    public void The_number_is_written_into_the_heading_as_text()
    {
        string html = Renderer.Render(Document, HeadingNumbering.FromHeading1).Html;

        html.ShouldContain("""<span class="mq-heading-number">1  </span>Introduction""");
        html.ShouldContain("""<span class="mq-heading-number">1.1  </span>Why it exists""");
    }

    [Fact]
    public void Nothing_is_written_when_numbering_is_off()
    {
        Renderer.Render(Document, HeadingNumbering.Off).Html
            .ShouldNotContain("mq-heading-number");

        // The one-argument overload is what every caller that has no opinion still uses.
        Renderer.Render(Document).Html.ShouldNotContain("mq-heading-number");
    }

    /// <summary>
    /// A heading above the level the count starts at keeps its own words and nothing else,
    /// which is what makes "leave the document's title alone" a real setting.
    /// </summary>
    [Fact]
    public void A_heading_above_the_numbered_range_is_left_alone()
    {
        string html = Renderer.Render(Document, HeadingNumbering.FromHeading2).Html;

        html.ShouldContain(">Introduction<");
        html.ShouldContain("""<span class="mq-heading-number">1  </span>Why it exists""");
    }

    // ------------------------------------------------------------------ outline

    /// <summary>
    /// The number and the words stay apart. The panel dims one and filters on the other, and
    /// a heading that arrived as "1.1 Why it exists" can do neither.
    /// </summary>
    [Fact]
    public void The_outline_keeps_the_number_out_of_the_headings_text()
    {
        IReadOnlyList<OutlineHeading> outline =
            Renderer.Render(Document, HeadingNumbering.FromHeading1).Outline;

        OutlineHeading why = outline.Single(h => h.Number == "1.1");

        why.Text.ShouldBe("Why it exists");
        outline[0].Text.ShouldBe("Introduction");
        outline[0].Number.ShouldBe("1");
    }

    /// <summary>
    /// A heading with no words still takes a number, and still stays out of the panel.
    ///
    /// Two rules that look like they disagree and do not: the outline lists headings a
    /// reader could click, and an empty one has nothing to show; the numbering counts
    /// sections, and skipping one would shift every number after it. The shell counted every
    /// heading element it could see, so this is also what the preview has always done.
    /// </summary>
    [Fact]
    public void An_empty_heading_is_counted_but_not_listed()
    {
        const string markdown = """
            # One

            ##

            ## Two
            """;

        RenderedMarkdown rendered = Renderer.Render(markdown, HeadingNumbering.FromHeading1);

        rendered.Html.ShouldContain("""<span class="mq-heading-number">1.2  </span>Two""");
        rendered.Outline.Select(h => h.Text).ShouldBe(["One", "Two"]);
    }

    [Fact]
    public void An_unnumbered_document_reports_no_numbers()
    {
        Renderer.Render(Document, HeadingNumbering.Off).Outline
            .ShouldAllBe(h => h.Number == string.Empty);
    }

    [Fact]
    public void A_heading_above_the_numbered_range_reports_no_number()
    {
        IReadOnlyList<OutlineHeading> outline =
            Renderer.Render(Document, HeadingNumbering.FromHeading2).Outline;

        outline.First(h => h.Text == "Introduction").Number.ShouldBe(string.Empty);
        outline.First(h => h.Text == "Why it exists").Number.ShouldBe("1");
    }
}
