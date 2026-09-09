// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folio.Tests;

/// <summary>
/// The round trip, and the fact that a Folio is the one file Marqora reads that somebody else
/// wrote. Half of these are the journey working; the other half are it being lied to.
/// </summary>
public sealed class FolioRoundTripTests
{
    /// <summary>A one-pixel PNG, so that the bytes really are an image when they are sniffed.</summary>
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
    ];

    private static string Page(string body, FolioPayload payload) =>
        $"""
         <!DOCTYPE html>
         <html><head>{FolioMarkup.MarkerMeta}</head>
         <body>{body}
         {FolioPayload.Encode(payload)}
         </body></html>
         """;

    private static FolioPayload PayloadOf(params (string Entry, string Text)[] documents) => new()
    {
        Documents = [.. documents.Select(d => new FolioPayloadDocument { Entry = d.Entry, Text = d.Text })],
    };

    private static string Image(string entry) =>
        $"<img data-mq-asset=\"{entry}\" src=\"data:image/png;base64,{Convert.ToBase64String(Png)}\" />";

    private static string Folder() =>
        Path.Combine(Path.GetTempPath(), "marqora-folio-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public void A_payload_survives_being_encoded_and_read_back()
    {
        FolioPayload? read = FolioPayload.TryDecode(
            Page("<p>hello</p>", PayloadOf(("guide.md", "# Guide\n\nHello."))));

        read.ShouldNotBeNull();
        read.Documents.ShouldHaveSingleItem().Text.ShouldBe("# Guide\n\nHello.");
    }

    /// <summary>
    /// The reason the payload is base64. A document about markup contains the characters that
    /// end a script element, and raw JSON here would be cut off at the first one - the rest of
    /// the document then parsed as live markup.
    /// </summary>
    [Fact]
    public void A_document_containing_a_closing_script_tag_survives()
    {
        const string Awkward = "Write `</script>` to finish it.\n\n</script><b>injected</b>";

        string html = Page("<p>x</p>", PayloadOf(("guide.md", Awkward)));

        // The dangerous sequence appears nowhere outside the document's own encoded form.
        html.IndexOf("</script>", StringComparison.Ordinal)
            .ShouldBe(html.LastIndexOf("</script>", StringComparison.Ordinal));

        FolioPayload.TryDecode(html)!.Documents[0].Text.ShouldBe(Awkward);
    }

    [Fact]
    public void An_ordinary_web_page_is_not_a_folio()
    {
        FolioPayload.TryDecode("<html><body><p>Just a page.</p></body></html>").ShouldBeNull();
        FolioPayload.IsFolio("<html><head><title>A page</title></head>").ShouldBeFalse();
    }

    [Fact]
    public void The_head_marker_is_what_identifies_a_folio()
    {
        string html = Page("<p>x</p>", PayloadOf(("guide.md", "hi")));

        FolioPayload.IsFolio(html[..Math.Min(400, html.Length)]).ShouldBeTrue();
    }

    [Fact]
    public void A_damaged_payload_is_refused_rather_than_half_read()
    {
        string html = Page("<p>x</p>", PayloadOf(("guide.md", "hi")))
            .Replace("</script>", "!!!</script>", StringComparison.Ordinal);

        FolioPayload.TryDecode(html).ShouldBeNull();
    }

    [Fact]
    public void Documents_and_images_come_back_out()
    {
        string target = Folder();

        string html = Page(
            $"<p>{Image("media/chart.png")}</p><p>{Image("images/logo.png")}</p>",
            PayloadOf(("guide.md", "![](media/chart.png)"), ("setup.md", "Setup.")));

        FolioUnpackResult result = FolioUnpacker.Unpack(html, FolioPayload.TryDecode(html)!, target);

        result.Documents.Count.ShouldBe(2);
        result.Images.ShouldBe(2);
        result.Refused.ShouldBeEmpty();

        File.ReadAllText(Path.Combine(target, "guide.md")).ShouldBe("![](media/chart.png)");
        File.ReadAllBytes(Path.Combine(target, "media", "chart.png")).ShouldBe(Png);
        File.Exists(Path.Combine(target, "images", "logo.png")).ShouldBeTrue();
    }

    [Fact]
    public void The_same_image_used_twice_is_written_once()
    {
        string target = Folder();

        string html = Page(
            $"{Image("media/chart.png")}{Image("media/chart.png")}",
            PayloadOf(("one.md", "a"), ("two.md", "b")));

        FolioUnpacker.Unpack(html, FolioPayload.TryDecode(html)!, target).Images.ShouldBe(1);
    }

    /// <summary>
    /// The one that matters. A Folio arrives by email and every path in it was written by
    /// somebody else, so an entry that climbs out of the folder is refused rather than followed.
    /// </summary>
    [Theory]
    [InlineData("../escaped.md")]
    [InlineData("..\\escaped.md")]
    [InlineData("sub/../../escaped.md")]
    [InlineData("C:\\Windows\\System32\\escaped.md")]
    [InlineData("/etc/passwd")]
    public void A_document_path_that_climbs_out_is_refused(string entry)
    {
        string target = Folder();
        string html = Page("<p>x</p>", PayloadOf((entry, "owned")));

        FolioUnpackResult result = FolioUnpacker.Unpack(html, FolioPayload.TryDecode(html)!, target);

        result.Documents.ShouldBeEmpty();
        result.Refused.ShouldHaveSingleItem().ShouldContain("does not stay inside");

        Directory.GetFiles(Path.GetDirectoryName(target)!, "escaped.md", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void An_image_path_that_climbs_out_is_refused()
    {
        string target = Folder();

        string html = Page(Image("../../escaped.png"), PayloadOf(("guide.md", "x")));

        FolioUnpackResult result = FolioUnpacker.Unpack(html, FolioPayload.TryDecode(html)!, target);

        result.Images.ShouldBe(0);
        result.Refused.ShouldHaveSingleItem().ShouldContain("does not stay inside");
    }

    /// <summary>
    /// The bytes decide what a file is, not the name it arrived under - the same boundary the
    /// asset store draws for a pasted image, and for the same reason.
    /// </summary>
    [Fact]
    public void Something_that_is_not_an_image_is_left_out_however_it_is_named()
    {
        string target = Folder();

        string executable = Convert.ToBase64String(Encoding.UTF8.GetBytes("MZ\u0090\0\u0003 not a picture"));
        string html = Page(
            $"<img data-mq-asset=\"media/innocent.png\" src=\"data:image/png;base64,{executable}\" />",
            PayloadOf(("guide.md", "x")));

        FolioUnpackResult result = FolioUnpacker.Unpack(html, FolioPayload.TryDecode(html)!, target);

        result.Images.ShouldBe(0);
        result.Refused.ShouldHaveSingleItem().ShouldContain("is not an image");
        File.Exists(Path.Combine(target, "media", "innocent.png")).ShouldBeFalse();
    }

    [Fact]
    public void Unpacking_refuses_a_folder_that_already_holds_work()
    {
        string target = Folder();

        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "important.md"), "do not lose me");

        string html = Page("<p>x</p>", PayloadOf(("guide.md", "x")));

        Should.Throw<IOException>(() => FolioUnpacker.Unpack(html, FolioPayload.TryDecode(html)!, target));

        File.ReadAllText(Path.Combine(target, "important.md")).ShouldBe("do not lose me");
    }

    [Fact]
    public void An_image_the_writer_could_not_embed_is_simply_absent()
    {
        string target = Folder();

        // No data-mq-asset attribute: the writer left this one as a link, so there are no bytes
        // to recover and nothing should be invented for it.
        string html = Page("<img src=\"media/too-big.png\" />", PayloadOf(("guide.md", "x")));

        FolioUnpackResult result = FolioUnpacker.Unpack(html, FolioPayload.TryDecode(html)!, target);

        result.Images.ShouldBe(0);
        result.Refused.ShouldBeEmpty();
    }
}
