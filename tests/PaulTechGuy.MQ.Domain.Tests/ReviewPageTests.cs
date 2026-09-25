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

        stamp.ShouldContain("Review of <span>&lt;Plans&gt; &amp; Notes.md</span> • Sep 24, 2026 2:32 PM • 6 comments");
        stamp.ShouldContain("data-source-sha256=\"a41c9e2\"");
        stamp.ShouldContain("Later edits to the source are not reflected here.");
    }

    [Fact]
    public void One_comment_is_singular() =>
        ReviewPage.Stamp("a.md", "now", "0000000", 1).ShouldContain("1 comment<");

    [Fact]
    public void The_stamp_carries_the_logo_only_when_given_one()
    {
        ReviewPage.Stamp("a.md", "now", "0000000", 1, "data:image/png;base64,AAAA")
            .ShouldContain("<img class=\"mq-review-logo\" src=\"data:image/png;base64,AAAA\" alt=\"Marqora\" />");
        ReviewPage.Stamp("a.md", "now", "0000000", 1).ShouldNotContain("<img");
    }

    /// <summary>A shortened name is still available whole, on hover.</summary>
    [Fact]
    public void A_long_name_is_shortened_in_the_stamp_and_whole_in_its_title()
    {
        const string name = "qzT7maKb9xR2vL38dioseusoeJEHp8Nc4WdY6sF1jHa3Bg5Ue0Xi7Po2Zr6.md";

        string stamp = ReviewPage.Stamp(name, "now", "0000000", 2);

        stamp.ShouldContain("<span title=\"" + name + "\">qzT7maKb9xR2vL3...Po2Zr6.md</span>");
    }

    [Theory]
    [InlineData("qzT7maKb9xR2vL38dioseusoeJEHp8Nc4WdY6sF1jHa3Bg5Ue0Xi7Po2Zr6.md", "qzT7maKb9xR2vL3...Po2Zr6.md")]
    [InlineData("SUMMARY.md", "SUMMARY.md")]
    [InlineData("A thirty character file name.md", "A thirty charac...e name.md")]
    [InlineData("Exactly thirty characters!!.md", "Exactly thirty characters!!.md")]
    [InlineData("no-extension-but-a-very-long-name-indeed", "no-extension-bu...indeed")]
    public void Long_file_names_keep_their_start_and_end(string name, string expected) =>
        ReviewPage.ShortFileName(name).ShouldBe(expected);

    /// <summary>A name made of emoji is cut between them, never through one.</summary>
    [Fact]
    public void Shortening_never_splits_a_character()
    {
        string name = string.Concat(Enumerable.Repeat("😀", 40)) + ".md";

        ReviewPage.ShortFileName(name).ShouldBe(
            string.Concat(Enumerable.Repeat("😀", 15)) + "..." + string.Concat(Enumerable.Repeat("😀", 6)) + ".md");
    }

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
    [InlineData("Offline-Sync.md", "Offline-Sync (review by Marqora).html")]
    [InlineData("notes.markdown", "notes (review by Marqora).html")]
    [InlineData("Untitled 2", "Untitled 2 (review by Marqora).html")]
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

            // Every segment of a comment lights together, on hover and when jumped to.
            css.ShouldContain($":has(mark.mq-comment[data-note=\"{n}\"]:hover) mark.mq-comment[data-note=\"{n}\"]");
            css.ShouldContain($":has(#mq-mark-{n}:target) mark.mq-comment[data-note=\"{n}\"]");
        }

        ReviewPage.HoverCss(0).ShouldBeEmpty();
    }

    /// <summary>The stamp lines up with the text and notes together when there is a measure.</summary>
    [Fact]
    public void The_stylesheet_follows_the_measure()
    {
        ReviewPage.Css(760).ShouldContain("max-width: calc(760px + var(--mq-note-width) + var(--mq-note-gap));");
        ReviewPage.Css(0).ShouldContain("max-width: none;");

        // A comment inside inline code keeps the code box's rounded shape, as in the preview.
        ReviewPage.Css(0).ShouldContain(".mq-review .mq-preview :not(pre) > code mark.mq-comment { border-radius: 3px; }");
    }
}
