// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

public class DocumentAssetsTests
{
    [Theory]
    [InlineData(ImageFolderMode.DocumentAssets, "guide.assets")]
    [InlineData(ImageFolderMode.SharedImages, "images")]
    [InlineData(ImageFolderMode.BesideDocument, "")]
    public void The_folder_follows_the_preference(ImageFolderMode mode, string expected)
    {
        DocumentAssets.FolderNameFor(@"C:\docs\guide.md", mode).ShouldBe(expected);
    }

    [Fact]
    public void A_document_name_with_spaces_produces_a_folder_without_them()
    {
        // A destination in CommonMark ends at the first space, so a folder called
        // "My Guide.assets" would break every reference into it.
        DocumentAssets.FolderNameFor(@"C:\docs\My Guide.md", ImageFolderMode.DocumentAssets)
            .ShouldBe("my-guide.assets");
    }

    [Fact]
    public void A_document_name_with_dots_keeps_only_its_own_stem()
    {
        DocumentAssets.FolderNameFor(@"C:\docs\v1.2.notes.md", ImageFolderMode.DocumentAssets)
            .ShouldBe("v1.2.notes.assets");
    }

    // ------------------------------------------------------------------------- slug

    [Theory]
    [InlineData("Screenshot 2026-09-05 141233", "screenshot-2026-09-05-141233")]
    [InlineData("logo", "logo")]
    [InlineData("a  b", "a-b")]
    // What Windows calls a file it duplicated. The space-hyphen-space used to come out as three
    // dashes, because keeping the hyphen told the collapsing that the last character was not one.
    [InlineData("test - Copy", "test-copy")]
    [InlineData("test - Copy (2)", "test-copy-2")]
    // A hyphen the author typed is still a hyphen; it just cannot stack with its neighbours.
    [InlineData("well-known", "well-known")]
    [InlineData("a -- b", "a-b")]
    [InlineData("a - - b", "a-b")]
    [InlineData("--lead and trail--", "lead-and-trail")]
    [InlineData("Ünïcødé", "n-c-d")]
    [InlineData("...", "image")]
    [InlineData("..", "image")]
    [InlineData("", "image")]
    [InlineData("   ", "image")]
    public void Names_are_slugged(string input, string expected)
    {
        DocumentAssets.Slug(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("lpt1")]
    public void A_reserved_device_name_is_not_used(string name)
    {
        // Windows will not give a file these names whatever the extension.
        DocumentAssets.Slug(name).ShouldBe("image");
    }

    [Fact]
    public void A_very_long_name_is_cut_short_without_a_trailing_dash()
    {
        string slug = DocumentAssets.Slug(new string('a', 200) + " tail");

        slug.Length.ShouldBe(60);
        slug.ShouldNotEndWith("-");
    }

    // ---------------------------------------------------------------------- naming

    [Fact]
    public void The_first_image_in_an_empty_folder_is_not_numbered()
    {
        DocumentAssets.NextFreeName("logo", ".png", []).ShouldBe("logo.png");
    }

    [Fact]
    public void A_taken_name_gets_the_next_number()
    {
        DocumentAssets.NextFreeName("image", ".png", ["image.png"]).ShouldBe("image-1.png");
    }

    [Fact]
    public void Numbering_continues_past_the_highest_rather_than_filling_a_gap()
    {
        // Reusing "image-2.png" would point an old export or another document at a different
        // picture under a name it already knows.
        DocumentAssets.NextFreeName("image", ".png", ["image.png", "image-1.png", "image-3.png"])
            .ShouldBe("image-4.png");
    }

    [Fact]
    public void Names_are_compared_without_case_because_the_filesystem_is()
    {
        DocumentAssets.NextFreeName("logo", ".png", ["LOGO.PNG"]).ShouldBe("logo-1.png");
    }

    [Fact]
    public void A_different_extension_does_not_share_the_numbering()
    {
        DocumentAssets.NextFreeName("logo", ".png", ["logo.svg"]).ShouldBe("logo.png");
    }

    [Fact]
    public void An_unrelated_name_that_merely_starts_the_same_does_not_raise_the_number()
    {
        DocumentAssets.NextFreeName("logo", ".png", ["logo.png", "logotype-9.png"])
            .ShouldBe("logo-1.png");
    }

    // ------------------------------------------------------------------- references

    // ------------------------------------------------- files already in place

    [Fact]
    public void A_file_already_in_the_assets_folder_is_referenced_rather_than_copied()
    {
        // Copying a picture that is already in the folder produces "test-1.jpg" beside "test.jpg"
        // - two identical files, with the document pointing at the copy.
        DocumentAssets.ExistingReferenceFor(
            @"C:\docs\guide.md", ImageFolderMode.DocumentAssets, @"C:\docs\guide.assets\test.jpg")
            .ShouldBe("guide.assets/test.jpg");
    }

    [Fact]
    public void A_file_beside_the_document_counts_when_that_is_where_images_go()
    {
        DocumentAssets.ExistingReferenceFor(
            @"C:\docs\guide.md", ImageFolderMode.BesideDocument, @"C:\docs\test.jpg")
            .ShouldBe("test.jpg");
    }

    [Fact]
    public void A_file_in_a_shared_images_folder_counts_too()
    {
        DocumentAssets.ExistingReferenceFor(
            @"C:\docs\guide.md", ImageFolderMode.SharedImages, @"C:\docs\images\test.jpg")
            .ShouldBe("images/test.jpg");
    }

    [Fact]
    public void The_existing_name_is_used_as_it_is_rather_than_slugged()
    {
        // The file exists and renaming it is not on offer; encoding deals with the space.
        DocumentAssets.ExistingReferenceFor(
            @"C:\docs\guide.md", ImageFolderMode.DocumentAssets, @"C:\docs\guide.assets\My Shot.jpg")
            .ShouldBe("guide.assets/My%20Shot.jpg");
    }

    [Theory]
    // Beside the document, but images go in the assets folder - so it still has to be copied in.
    [InlineData(@"C:\docs\test.jpg")]
    // One level up. Referencing it would need "../", which the preview will not serve.
    [InlineData(@"C:\shared\test.jpg")]
    // A different document's assets.
    [InlineData(@"C:\docs\notes.assets\test.jpg")]
    // Somewhere else entirely.
    [InlineData(@"D:\pictures\test.jpg")]
    public void A_file_anywhere_else_is_copied_in(string source)
    {
        DocumentAssets.ExistingReferenceFor(
            @"C:\docs\guide.md", ImageFolderMode.DocumentAssets, source).ShouldBeNull();
    }

    [Fact]
    public void A_bitmap_off_the_clipboard_has_no_source_to_compare()
    {
        DocumentAssets.ExistingReferenceFor(
            @"C:\docs\guide.md", ImageFolderMode.DocumentAssets, "").ShouldBeNull();
    }

    [Fact]
    public void The_comparison_ignores_case_because_the_filesystem_does()
    {
        DocumentAssets.ExistingReferenceFor(
            @"C:\docs\guide.md", ImageFolderMode.DocumentAssets, @"c:\DOCS\GUIDE.ASSETS\test.jpg")
            .ShouldBe("guide.assets/test.jpg");
    }

    [Fact]
    public void A_reference_joins_the_folder_and_the_file_with_a_forward_slash()
    {
        DocumentAssets.RelativeReference("guide.assets", "image-1.png")
            .ShouldBe("guide.assets/image-1.png");
    }

    [Fact]
    public void No_folder_means_just_the_file()
    {
        DocumentAssets.RelativeReference("", "image-1.png").ShouldBe("image-1.png");
    }

    [Fact]
    public void A_folder_the_user_named_is_encoded_because_it_can_contain_spaces()
    {
        // The file half is minted by Slug and has no spaces; the folder half comes from the
        // document's own name under SharedImages or a hand-edited setting.
        DocumentAssets.RelativeReference("My Images", "shot.png")
            .ShouldBe("My%20Images/shot.png");
    }

    [Fact]
    public void Dots_dashes_and_underscores_survive_encoding_because_they_are_legible()
    {
        DocumentAssets.RelativeReference("guide.assets", "a-b_c.png")
            .ShouldBe("guide.assets/a-b_c.png");
    }
}
