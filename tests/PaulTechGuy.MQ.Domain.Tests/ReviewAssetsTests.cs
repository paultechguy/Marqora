// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The pictures a resumed review takes back out of its page: one spelling of each path for
/// every side that looks one up, the bytes deciding what is an image, and only the article read.
/// </summary>
public sealed class ReviewAssetsTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private static string Img(string entry, byte[] bytes, string declared = "image/png") =>
        $"<img data-mq-asset=\"{entry}\" src=\"data:{declared};base64,{Convert.ToBase64String(bytes)}\" alt=\"\" />";

    private static string Page(string article, string source = "Text.\n") =>
        "<html><body><article class=\"mq-preview\">" + article + "</article>\n"
        + ReviewPage.SourceBlock(source) + "\n</body></html>";

    [Theory]
    [InlineData("img/a.png", "img/a.png")]
    [InlineData("./img/a.png", "img/a.png")]
    [InlineData("img/a%20b.png", "img/a b.png")]
    [InlineData("img/a+b.png", "img/a+b.png")]
    [InlineData("img/a&amp;b.png", "img/a&b.png")]
    [InlineData(@"img\sub\a.png", "img/sub/a.png")]
    [InlineData("img/../pics/a.png", "pics/a.png")]
    [InlineData("img/a.png?v=2", "img/a.png")]
    public void A_path_has_one_spelling_whoever_asks(string reference, string expected)
    {
        ReviewAssets.NormalizeKey(reference).ShouldBe(expected);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("/rooted.png")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("https://example.com/a.png")]
    [InlineData("")]
    public void A_path_that_leaves_the_document_has_no_key(string reference)
    {
        ReviewAssets.NormalizeKey(reference).ShouldBeNull();
    }

    [Fact]
    public void Images_in_the_article_are_collected_by_their_bytes()
    {
        var assets = ReviewAssets.Extract(Page(Img("./img/a%20b.png", Png, declared: "text/html")));

        assets.Count.ShouldBe(1);
        assets["IMG/A B.PNG"].MediaType.ShouldBe("image/png");
        assets["img/a b.png"].Bytes.ShouldBe(Png);
    }

    /// <summary>A data URI claiming to be an image is a claim; bytes that are not one are never served.</summary>
    [Fact]
    public void Bytes_that_are_not_an_image_are_left_out()
    {
        byte[] html = "<html><script>alert(1)</script></html>"u8.ToArray();

        ReviewAssets.Extract(Page(Img("page.png", html))).ShouldBeEmpty();
    }

    /// <summary>The CriticMarkup block is the document's own text, and a document can hold an img like these.</summary>
    [Fact]
    public void Nothing_is_collected_from_the_source_block()
    {
        ReviewAssets.Extract(Page("<p>No pictures.</p>", source: Img("planted.png", Png))).ShouldBeEmpty();
    }

    [Fact]
    public void The_last_image_for_a_path_wins()
    {
        byte[] second = [.. Png, 9];

        var assets = ReviewAssets.Extract(Page(Img("a.png", Png) + Img("./a.png", second)));

        assets["a.png"].Bytes.ShouldBe(second);
    }
}
