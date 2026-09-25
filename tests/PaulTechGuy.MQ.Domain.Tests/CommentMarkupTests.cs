// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The five styles a review comment may carry, read the way the preview reads them, and the
/// shortcut keys that write them.
/// </summary>
public sealed class CommentMarkupTests
{
    private static string Describe(string text) =>
        string.Join(" | ", CommentMarkup.Parse(text).Select(s => $"{s.Style}:{s.Text}"));

    [Theory]
    [InlineData("**bold**", "Bold:bold")]
    [InlineData("*italic*", "Italic:italic")]
    [InlineData("++under++", "Underline:under")]
    [InlineData("==lit==", "Highlight:lit")]
    [InlineData("`code()`", "Code:code()")]
    public void Each_style_is_read(string text, string expected) =>
        Describe(text).ShouldBe(expected);

    [Fact]
    public void Plain_text_around_a_style_is_kept() =>
        Describe("say **this** now").ShouldBe("None:say  | Bold:this | None: now");

    /// <summary>Styles nest, so italic around bold is italic text with a bold word in it.</summary>
    [Fact]
    public void Styles_nest() =>
        Describe("*a **b** c*").ShouldBe("Italic:a  | Bold, Italic:b | Italic: c");

    /// <summary>Inside code every character is literal, markers included.</summary>
    [Fact]
    public void Nothing_is_read_inside_code() =>
        Describe("`**x**`").ShouldBe("Code:**x**");

    /// <summary>A marker with no partner means what it says.</summary>
    [Theory]
    [InlineData("2 * 3 = 6")]
    [InlineData("i++ and a == b")]
    [InlineData("****")]
    [InlineData("one ` tick")]
    public void An_unpartnered_marker_is_text(string text) =>
        Describe(text).ShouldBe("None:" + text);

    /// <summary>A pair does not reach across a line break.</summary>
    [Fact]
    public void A_style_does_not_run_across_lines() =>
        Describe("**a\nb**").ShouldBe("None:**a\nb**");

    [Fact]
    public void Html_wraps_each_style_and_encodes_the_text() =>
        CommentMarkup.ToHtml("**<b>** ==hi== `a<b` ++u++ *i*")
            .ShouldBe("<strong>&lt;b&gt;</strong> <mark>hi</mark> <code>a&lt;b</code> <u>u</u> <em>i</em>");

    [Fact]
    public void Html_keeps_paragraphs_and_line_breaks() =>
        CommentMarkup.ToHtml("one\ntwo\n\nthree")
            .ShouldBe("one<br>two<span class=\"mq-sidenote-para\">three</span>");

    [Fact]
    public void Toggling_wraps_the_selection() =>
        CommentMarkup.Toggle("make this bold", 5, 4, CommentStyle.Bold)
            .ShouldBe(("make **this** bold", 7, 4));

    /// <summary>A double-click selects the word, not its markers; toggling again takes them off.</summary>
    [Fact]
    public void Toggling_a_word_already_wearing_the_style_removes_it() =>
        CommentMarkup.Toggle("make **this** bold", 7, 4, CommentStyle.Bold)
            .ShouldBe(("make this bold", 5, 4));

    [Fact]
    public void Toggling_a_selection_that_includes_the_markers_removes_them() =>
        CommentMarkup.Toggle("==hot==", 0, 7, CommentStyle.Highlight)
            .ShouldBe(("hot", 0, 3));

    /// <summary>Nothing selected: an empty pair with the caret between, ready to type into.</summary>
    [Fact]
    public void Toggling_with_nothing_selected_puts_down_an_empty_pair() =>
        CommentMarkup.Toggle("ab", 1, 0, CommentStyle.Code)
            .ShouldBe(("a``b", 2, 0));
}
