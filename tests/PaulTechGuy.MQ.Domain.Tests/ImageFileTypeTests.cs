// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

public class ImageFileTypeTests
{
    private static readonly byte[] Png =
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    [Fact]
    public void A_png_is_recognized()
    {
        ImageFileTypes.ExtensionFor(Png).ShouldBe(".png");
    }

    [Fact]
    public void A_jpeg_is_recognized()
    {
        ImageFileTypes.ExtensionFor([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]).ShouldBe(".jpg");
    }

    [Theory]
    [InlineData("GIF87a")]
    [InlineData("GIF89a")]
    public void Both_gif_versions_are_recognized(string header)
    {
        ImageFileTypes.ExtensionFor(Encoding.ASCII.GetBytes(header + "\0\0\0")).ShouldBe(".gif");
    }

    [Fact]
    public void A_bitmap_is_recognized()
    {
        ImageFileTypes.ExtensionFor(Encoding.ASCII.GetBytes("BM\0\0\0\0")).ShouldBe(".bmp");
    }

    [Fact]
    public void A_webp_is_recognized_by_the_tag_after_the_size()
    {
        // RIFF, four bytes of length, then the format. Checking where the tag actually is
        // rather than scanning for it is what stops a RIFF wave file matching.
        ImageFileTypes.ExtensionFor(Encoding.ASCII.GetBytes("RIFFWEBPVP8 "))
            .ShouldBe(".webp");
    }

    [Fact]
    public void A_riff_that_is_not_a_webp_is_not_an_image()
    {
        ImageFileTypes.ExtensionFor(Encoding.ASCII.GetBytes("RIFFWAVEfmt "))
            .ShouldBeNull();
    }

    [Fact]
    public void An_avif_is_recognized()
    {
        ImageFileTypes.ExtensionFor(Encoding.ASCII.GetBytes("\0\0\0 ftypavifavif"))
            .ShouldBe(".avif");
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")]
    [InlineData("<?xml version=\"1.0\"?><svg></svg>")]
    [InlineData("<!-- a comment --><SVG></SVG>")]
    public void Svg_is_recognized_despite_having_no_signature(string markup)
    {
        ImageFileTypes.ExtensionFor(Encoding.UTF8.GetBytes(markup)).ShouldBe(".svg");
    }

    [Fact]
    public void A_byte_order_mark_does_not_hide_an_svg()
    {
        byte[] withMark = [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. Encoding.UTF8.GetBytes("<svg></svg>")];

        ImageFileTypes.ExtensionFor(withMark).ShouldBe(".svg");
    }

    [Fact]
    public void Html_that_merely_mentions_svg_later_on_is_not_an_svg()
    {
        // The tag has to be near the front, past a declaration or a comment - not anywhere in
        // a document that happens to talk about one.
        string html = "<html><body>" + new string('x', 2000) + "<svg></svg></body></html>";

        ImageFileTypes.ExtensionFor(Encoding.UTF8.GetBytes(html)).ShouldBeNull();
    }

    // --------------------------------------------------------------- the refusals

    [Fact]
    public void An_executable_is_refused_however_it_is_named()
    {
        // The whole point of sniffing: a .exe renamed .png must never be written beside a
        // document because a drop said it was an image.
        ImageFileTypes.ExtensionFor(Encoding.ASCII.GetBytes("MZ\0\0\0\0"))
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("#!/bin/sh\necho hi")]
    [InlineData("Write-Host 'hi'")]
    [InlineData("plain text")]
    [InlineData("")]
    public void Anything_that_is_not_an_image_is_refused(string content)
    {
        ImageFileTypes.ExtensionFor(Encoding.UTF8.GetBytes(content)).ShouldBeNull();
    }

    [Fact]
    public void A_truncated_header_is_refused_rather_than_throwing()
    {
        ImageFileTypes.ExtensionFor([0x89, (byte)'P']).ShouldBeNull();
    }

    // ------------------------------------------------------------- the allow list

    [Theory]
    [InlineData("a.png", true)]
    [InlineData("a.SVG", true)]
    [InlineData("a.jpeg", true)]
    [InlineData("a.exe", false)]
    [InlineData("a.lnk", false)]
    [InlineData("a.ps1", false)]
    [InlineData("a", false)]
    [InlineData(null, false)]
    public void The_extension_allow_list_is_the_second_gate(string? name, bool allowed)
    {
        ImageFileTypes.IsAllowedExtension(name).ShouldBe(allowed);
    }

    [Fact]
    public void Every_extension_the_sniffer_can_return_is_on_the_allow_list()
    {
        // The two gates must agree, or the store would accept bytes and then refuse to name them.
        foreach (string extension in new[] { ".png", ".jpg", ".gif", ".bmp", ".webp", ".avif", ".svg" })
        {
            ImageFileTypes.AllowedExtensions.ShouldContain(extension);
        }
    }
}
