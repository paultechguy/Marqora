// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Rendering.Tests;

/// <summary>
/// The anchor a heading gets, and the one case <c>UseAdvancedExtensions</c>'s own
/// auto-identifier extension gets wrong: it collapses a run of separators left behind by a
/// removed character into a single hyphen, and real GitHub does not. A hand-written table of
/// contents - airbnb/javascript's own README links "Comparison Operators &amp; Equality" to
/// "#comparison-operators--equality" - is written against the real thing, so an exported
/// document's internal links have to resolve the same way.
/// </summary>
public class GitHubHeadingSlugTests
{
    private static readonly MarkdigMarkdownRenderer Renderer =
        new(NullLogger<MarkdigMarkdownRenderer>.Instance);

    private static string SlugOf(string markdown) =>
        Renderer.Render(markdown).Outline.Single().Slug;

    [Theory]
    [InlineData("## Comparison Operators & Equality", "comparison-operators--equality")]
    [InlineData("## Paragraphs, Line Breaks & Whitespace", "paragraphs-line-breaks--whitespace")]
    [InlineData("## Foo_Bar", "foo_bar")]
    public void A_removed_character_leaves_its_hyphen_behind_rather_than_collapsing(
        string markdown, string expected) =>
        SlugOf(markdown).ShouldBe(expected);

    /// <summary>
    /// <c>{#short}</c> - Markdig's generic-attributes syntax - is a promise made to whatever
    /// already links against it. Correcting it into something the author never asked for would
    /// break every one of those links instead of fixing them.
    /// </summary>
    [Fact]
    public void An_explicit_heading_id_is_left_exactly_as_written() =>
        SlugOf("# A Very Long Title {#short}").ShouldBe("short");

    /// <summary>
    /// An explicit id still reserves its slug, so a later heading that would naturally compute
    /// the same one is renumbered instead of silently landing on it too.
    /// </summary>
    [Fact]
    public void An_explicit_id_reserves_its_slug_against_a_later_collision()
    {
        var outline = Renderer.Render("# Foo {#foo}\n\n# Foo\n").Outline;

        outline[0].Slug.ShouldBe("foo");
        outline[1].Slug.ShouldBe("foo-1");
    }

    /// <summary>
    /// Two headings whose corrected slugs collide are numbered "-1", "-2" the way GitHub itself
    /// numbers a repeated heading - not "-1" twice, and not left to collide silently.
    /// </summary>
    [Fact]
    public void Headings_that_collide_only_after_correction_are_still_numbered()
    {
        var outline = Renderer.Render("## Foo & Bar\n\n## Foo & Bar\n").Outline;

        outline[0].Slug.ShouldBe("foo--bar");
        outline[1].Slug.ShouldBe("foo--bar-1");
    }

    /// <summary>
    /// <c>*[HTML]: ...</c> turns every later "HTML" into an <c>AbbreviationInline</c> - a leaf,
    /// not a container, and not the <c>LiteralInline</c> the plain-text walk otherwise expects.
    /// Skipping it silently dropped the word from the heading's own slug.
    /// </summary>
    [Fact]
    public void An_abbreviation_inside_a_heading_contributes_its_short_form() =>
        SlugOf("*[HTML]: HyperText Markup Language\n\n## Raw HTML\n").ShouldBe("raw-html");
}
