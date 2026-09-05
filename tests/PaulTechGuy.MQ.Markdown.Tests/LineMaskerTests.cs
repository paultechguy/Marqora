// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Markdown;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Markdown.Tests;

/// <summary>
/// The masker exists so that a spell engine is handed prose and nothing else, and its output is
/// only useful because the offsets it reports still point at the right characters in the original
/// line.
///
/// Two classes of bug are worth catching here. The first is a mask that changes a line's length,
/// which puts every squiggle in the wrong column and is invisible until someone looks closely at
/// a document with a link in it. The second is a rule that is too greedy — one that eats the link
/// text along with the target, or reads "$5 and $10" as a formula — which silently stops whole
/// sentences being checked and looks exactly like a spell checker that works.
/// </summary>
public sealed class LineMaskerTests
{
    // ------------------------------------------------------------------ the length contract

    [Theory]
    [InlineData("Plain prose with nothing to mask.")]
    [InlineData("A `code span` in the middle.")]
    [InlineData("See [the guide](./guide.md) for more.")]
    [InlineData("![a diagram](./diagram.png) above.")]
    [InlineData("[ref]: https://example.com \"The Title\"")]
    [InlineData("[^note]: The footnote body.")]
    [InlineData("A footnote[^1] here.")]
    [InlineData("Visit <https://example.com> today.")]
    [InlineData("Some <span class=\"x\">marked</span> text.")]
    [InlineData("Go to https://example.com/a/b?c=d now.")]
    [InlineData("The formula $E = mc^2$ is famous.")]
    [InlineData("Display $$a + b = c$$ maths.")]
    [InlineData("Tom &amp; Jerry &#8212; friends.")]
    [InlineData("Nice work :sparkles: indeed.")]
    [InlineData("Unclosed `backtick stays put.")]
    [InlineData("")]
    public void Masking_never_changes_the_length_of_a_line(string line)
    {
        // Every offset the engine reports is an offset into the original line. A mask that
        // shortened or lengthened one would move every squiggle after it.
        Mask(line).Length.ShouldBe(line.Length);
    }

    [Fact]
    public void Every_character_that_survives_stays_in_its_own_column()
    {
        const string Line = "See [the guide](./guide.md) for more.";

        string masked = Mask(Line);

        masked.IndexOf("for more.", StringComparison.Ordinal)
            .ShouldBe(Line.IndexOf("for more.", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------ what is masked

    [Fact]
    public void A_code_span_is_masked()
    {
        Mask("Call `Analyze()` first.").ShouldNotContain("Analyze");
    }

    [Fact]
    public void A_link_target_is_masked_but_the_link_text_is_not()
    {
        string masked = Mask("See [the guide](./guide.md) for more.");

        masked.ShouldContain("the guide");
        masked.ShouldNotContain("guide.md");
    }

    [Fact]
    public void An_images_alt_text_is_kept_because_a_reader_sees_it()
    {
        string masked = Mask("![a network diagram](./diagram.png)");

        masked.ShouldContain("a network diagram");
        masked.ShouldNotContain("diagram.png");
    }

    [Fact]
    public void A_reference_definition_keeps_its_title_and_loses_its_target()
    {
        string masked = Mask("[ref]: https://example.com \"The Written Title\"");

        masked.ShouldContain("The Written Title");
        masked.ShouldNotContain("example.com");
        masked.ShouldNotContain("ref");
    }

    [Fact]
    public void A_footnote_definition_loses_its_marker_and_keeps_its_body()
    {
        // The body is prose the reader reads, so only the "[^note]:" marker goes.
        string masked = Mask("[^note]: Consulted in September.");

        masked.ShouldContain("Consulted in September.");
        masked.ShouldNotContain("note");
    }

    [Fact]
    public void A_reference_style_label_is_masked()
    {
        string masked = Mask("See [the guide][guideref] for more.");

        masked.ShouldContain("the guide");
        masked.ShouldNotContain("guideref");
    }

    [Theory]
    [InlineData("A footnote[^longlabel] here.", "longlabel")]
    [InlineData("Visit <https://example.com> today.", "example")]
    [InlineData("Some <span class=\"classname\">kept</span> text.", "classname")]
    [InlineData("Go to https://example.com/deep/path now.", "deep")]
    [InlineData("Also www.example.com counts.", "example")]
    [InlineData("The formula $E = mcsquared$ is famous.", "mcsquared")]
    [InlineData("Display $$alpha + beta$$ maths.", "alpha")]
    [InlineData("Tom &amp; Jerry.", "amp")]
    [InlineData("Nice work :facepunch: indeed.", "facepunch")]
    public void Non_prose_runs_are_masked(string line, string shouldBeGone)
    {
        Mask(line).ShouldNotContain(shouldBeGone);
    }

    [Fact]
    public void The_text_between_two_html_tags_survives()
    {
        Mask("Some <span class=\"x\">marked up</span> text.").ShouldContain("marked up");
    }

    // -------------------------------------------------------------------- what is left alone

    [Fact]
    public void Plain_prose_is_returned_unchanged()
    {
        const string Line = "The quick brown fox jumps over the lazy dog.";

        Mask(Line).ShouldBe(Line);
    }

    [Fact]
    public void Two_dollar_amounts_are_not_a_formula()
    {
        // "$5 and $10" is written identically to inline maths, and reading it as maths would
        // quietly stop "and" - and anything else between two prices - being checked. The
        // delimiters have to hug their content, which is the rule KaTeX itself uses.
        Mask("It cost $5 and then $10 more.").ShouldContain("and then");
    }

    [Fact]
    public void A_backtick_with_no_partner_masks_nothing()
    {
        const string Line = "An unclosed `backtick stays put.";

        Mask(Line).ShouldBe(Line);
    }

    [Fact]
    public void Emphasis_markers_are_left_where_they_are()
    {
        // The text inside emphasis is prose. Only the markers are punctuation, and the engine
        // splits on those by itself.
        Mask("This is **important** and *urgent*.").ShouldContain("important");
    }

    // ---------------------------------------------------------------------- code spans alone

    [Fact]
    public void MaskCodeSpans_leaves_everything_but_code_spans_alone()
    {
        // The style checks use this one on its own, and must keep seeing exactly what they
        // always have.
        string masked = LineMasker.MaskCodeSpans("See [the guide](./guide.md) and `code`.");

        masked.ShouldContain("guide.md");
        masked.ShouldNotContain("code");
    }

    private static string Mask(string line) => LineMasker.MaskNonProse(line);
}
