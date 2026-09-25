// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The text parts of a shared review page: the source block that has to stay inert and intact
/// whatever the document says, the stamp, and the names.
/// </summary>
public sealed class ReviewPageTests
{
    /// <summary>The whole of what sits between the source block's tags.</summary>
    private static string Inside(string block)
    {
        int open = block.IndexOf('>', StringComparison.Ordinal) + 1;
        int close = block.LastIndexOf("</script>", StringComparison.Ordinal);

        return block[open..close];
    }

    /// <summary>A document about HTML cannot end the block early.</summary>
    [Fact]
    public void A_closing_script_tag_in_the_source_cannot_end_the_block()
    {
        string block = ReviewPage.SourceBlock("Use </script> to close, or </SCRIPT >.\n");

        Inside(block).ShouldNotContain("</script", Case.Insensitive);
        Inside(block).ShouldContain("<\\/script>");
        Inside(block).ShouldContain("<\\/SCRIPT >");
    }

    /// <summary>
    /// A comment opener followed by a script tag would put the parser where the real closing tag
    /// no longer closes anything, and the rest of the page would vanish into the block.
    /// </summary>
    [Fact]
    public void A_comment_opener_and_script_tag_are_both_defused()
    {
        string inside = Inside(ReviewPage.SourceBlock("<!-- example: <script>alert(1)</script> -->"));

        inside.ShouldNotContain("<!--");
        inside.ShouldNotContain("<script", Case.Insensitive);
        inside.ShouldNotContain("</script", Case.Insensitive);
        inside.ShouldContain("<\\!-- example: <\\script>alert(1)<\\/script> -->");
    }

    /// <summary>Everything else is carried as written, so a reader of View Source sees markdown.</summary>
    [Fact]
    public void Ordinary_markdown_is_carried_verbatim()
    {
        const string markdown = "# Title\n\nSome {==text==}{>>a note<<} & <b>bold</b>.\n";

        Inside(ReviewPage.SourceBlock(markdown)).ShouldBe("\n" + markdown);
    }

    [Fact]
    public void The_source_block_is_inert_and_findable()
    {
        string block = ReviewPage.SourceBlock("x");

        block.ShouldStartWith("<script type=\"text/markdown\" id=\"mq-review-source\" data-format=\"criticmarkup\">");
        block.ShouldEndWith("x\n</script>");
    }

    /// <summary>The file name and date are the reader's text, not markup.</summary>
    [Fact]
    public void The_stamp_encodes_what_it_is_given()
    {
        string stamp = ReviewPage.Stamp("<Plans> & Notes.md", "Sep 24, 2026 2:32 PM", "a41c9e2", 6);

        stamp.ShouldContain("&lt;Plans&gt; &amp; Notes.md");
        stamp.ShouldContain("reviewed Sep 24, 2026 2:32 PM");
        stamp.ShouldContain("sha256:a41c9e2");
        stamp.ShouldContain("6 comments");
    }

    [Fact]
    public void One_comment_is_singular() =>
        ReviewPage.Stamp("a.md", "now", "0000000", 1).ShouldContain("1 comment<");

    [Fact]
    public void The_short_hash_is_seven_lowercase_hex_digits()
    {
        string hash = ReviewPage.ShortHash("# Doc\n");

        hash.Length.ShouldBe(7);
        hash.ShouldMatch("^[0-9a-f]{7}$");
        ReviewPage.ShortHash("# Doc\n").ShouldBe(hash);
        ReviewPage.ShortHash("# Doc!\n").ShouldNotBe(hash);
    }

    [Theory]
    [InlineData("Offline-Sync.md", "Offline-Sync (review).html")]
    [InlineData("notes.markdown", "notes (review).html")]
    [InlineData("Untitled 2", "Untitled 2 (review).html")]
    public void The_page_is_named_after_the_document(string document, string expected) =>
        ReviewPage.FileName(document).ShouldBe(expected);

    /// <summary>Every comment gets its hover pair, both ways.</summary>
    [Fact]
    public void Hover_rules_pair_every_note_with_its_highlight()
    {
        string css = ReviewPage.HoverCss(3);

        for (int n = 1; n <= 3; n++)
        {
            css.ShouldContain($"mark.mq-comment[data-note=\"{n}\"]:hover) #mq-note-{n}");
            css.ShouldContain($":has(#mq-note-{n}:hover) mark.mq-comment[data-note=\"{n}\"]");
        }

        ReviewPage.HoverCss(0).ShouldBeEmpty();
    }

    /// <summary>The stamp lines up with the text and notes together when there is a measure.</summary>
    [Fact]
    public void The_stylesheet_follows_the_measure()
    {
        ReviewPage.Css(760).ShouldContain("max-width: calc(760px + var(--mq-note-width) + var(--mq-note-gap));");
        ReviewPage.Css(0).ShouldContain("max-width: none;");
    }
}
