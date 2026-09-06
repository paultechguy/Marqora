// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

public sealed class DocumentAssetStoreTests : IDisposable
{
    private static readonly byte[] Png =
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly TempFolder _folder = new();
    private readonly DocumentAssetStore _store = new(NullLogger<DocumentAssetStore>.Instance);

    private string DocumentPath => Path.Combine(_folder.Path, "guide.md");

    private Task<string?> SaveAsync(
        byte[]? bytes = null,
        string? name = null,
        ImageFolderMode mode = ImageFolderMode.DocumentAssets) =>
        _store.SaveAsync(DocumentPath, bytes ?? Png, name, mode, TestContext.Current.CancellationToken);

    [Fact]
    public async Task An_image_lands_in_the_documents_own_assets_folder()
    {
        string? reference = await SaveAsync();

        reference.ShouldBe("guide.assets/image.png");
        File.Exists(Path.Combine(_folder.Path, "guide.assets", "image.png")).ShouldBeTrue();
    }

    [Fact]
    public async Task The_bytes_are_written_exactly_as_given()
    {
        await SaveAsync();

        File.ReadAllBytes(Path.Combine(_folder.Path, "guide.assets", "image.png"))
            .ShouldBe(Png);
    }

    [Theory]
    [InlineData(ImageFolderMode.SharedImages, "images/image.png")]
    [InlineData(ImageFolderMode.BesideDocument, "image.png")]
    public async Task The_folder_follows_the_preference(ImageFolderMode mode, string expected)
    {
        (await SaveAsync(mode: mode)).ShouldBe(expected);
    }

    [Fact]
    public async Task A_second_paste_does_not_overwrite_the_first()
    {
        await SaveAsync();

        (await SaveAsync()).ShouldBe("guide.assets/image-1.png");

        Directory.GetFiles(Path.Combine(_folder.Path, "guide.assets")).Length.ShouldBe(2);
    }

    [Fact]
    public async Task A_copied_file_keeps_its_own_name_slugged()
    {
        (await SaveAsync(name: "Screenshot 2026-09-05.png"))
            .ShouldBe("guide.assets/screenshot-2026-09-05.png");
    }

    [Fact]
    public async Task A_name_already_taken_on_disk_gets_the_next_number()
    {
        Directory.CreateDirectory(Path.Combine(_folder.Path, "guide.assets"));
        File.WriteAllBytes(Path.Combine(_folder.Path, "guide.assets", "logo.png"), Png);

        (await SaveAsync(name: "logo.png")).ShouldBe("guide.assets/logo-1.png");
    }

    [Fact]
    public async Task The_extension_comes_from_the_bytes_not_from_the_name()
    {
        // A file called .jpg whose bytes are a PNG is written as a PNG, because that is what
        // the preview will have to decode.
        (await SaveAsync(name: "photo.jpg")).ShouldBe("guide.assets/photo.png");
    }

    [Fact]
    public async Task An_executable_is_refused_however_it_is_named()
    {
        // The security boundary: a paste must never drop a runnable file beside a document.
        byte[] exe = Encoding.ASCII.GetBytes("MZ\0\0\0\0");

        (await SaveAsync(exe, "totally-an-image.png")).ShouldBeNull();
        Directory.Exists(Path.Combine(_folder.Path, "guide.assets")).ShouldBeFalse();
    }

    [Fact]
    public async Task Nothing_is_written_when_the_bytes_are_refused()
    {
        await SaveAsync(Encoding.UTF8.GetBytes("just some text"), "notes.png");

        Directory.GetFiles(_folder.Path).ShouldNotContain(f => f.EndsWith(".png", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"..\..\escaped.png")]
    [InlineData("../../escaped.png")]
    [InlineData(@"C:\Windows\System32\escaped.png")]
    public async Task A_name_trying_to_climb_out_of_the_folder_cannot(string name)
    {
        string? reference = await SaveAsync(name: name);

        // Not refused: the bytes are a real image and the user did paste them. The directory
        // half of the name is simply discarded, so what is left is a file name and nothing else.
        // The property that matters is where it landed, not what it ended up called.
        reference.ShouldNotBeNull();
        reference.ShouldNotContain("..");

        string written = Path.GetFullPath(Path.Combine(_folder.Path, Uri.UnescapeDataString(reference)));
        string root = Path.GetFullPath(_folder.Path + Path.DirectorySeparatorChar);

        written.ShouldStartWith(root);
        File.Exists(written).ShouldBeTrue();
    }

    [Fact]
    public async Task A_document_that_has_never_been_saved_has_nowhere_to_write()
    {
        // The caller prompts for a save before getting here; this is the belt-and-braces half.
        await Should.ThrowAsync<ArgumentException>(() =>
            _store.SaveAsync("", Png, null, ImageFolderMode.DocumentAssets, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_svg_is_accepted_because_the_preview_can_render_one()
    {
        byte[] svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        (await SaveAsync(svg, "diagram.svg")).ShouldBe("guide.assets/diagram.svg");
    }

    [Fact]
    public async Task A_document_whose_name_has_spaces_gets_an_encoded_reference()
    {
        string spaced = Path.Combine(_folder.Path, "My Guide.md");

        string? reference = await _store.SaveAsync(
            spaced, Png, null, ImageFolderMode.DocumentAssets, TestContext.Current.CancellationToken);

        // The folder is slugged, so there is nothing left to encode - which is the point.
        reference.ShouldBe("my-guide.assets/image.png");
    }

    public void Dispose() => _folder.Dispose();
}
