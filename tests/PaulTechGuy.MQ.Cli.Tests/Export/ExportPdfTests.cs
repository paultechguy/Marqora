// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>
/// CLI-EX-PDF-01: export pdf's page follows its flags - the paper and orientation in the page's
/// size, the margin in where the text starts - and a flag outranks the settings. Each PDF is made
/// by a real browser and read back with the PDF engine built into Windows.
/// </summary>
[Collection(nameof(BrowserRunsAlone))]
public sealed class ExportPdfTests
{
    /// <summary>Pixels per inch the first page is drawn at, to find where its text starts.</summary>
    private const double Dpi = 192;

    /// <summary>A line long enough to reach both margins, under a heading that starts at the left one.</summary>
    private const string Document = "# Margins\n\nWide enough text to run from one margin to the other, so that the first line of this paragraph fills the measure of the page whatever its size.\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("letter", "portrait", PaperSize.Letter, PageOrientation.Portrait)]
    [InlineData("a4", "portrait", PaperSize.A4, PageOrientation.Portrait)]
    [InlineData("legal", "portrait", PaperSize.Legal, PageOrientation.Portrait)]
    [InlineData("a4", "landscape", PaperSize.A4, PageOrientation.Landscape)]
    public async Task The_page_is_the_paper_and_orientation_asked_for(string paper, string orientation, PaperSize size, PageOrientation turned)
    {
        using var workspace = new Workspace();
        workspace.Text("doc.md", Document);

        CliResult result = await CliRunner.RunAsync(["export", "pdf", "--paper", paper, "--orientation", orientation, "doc.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());

        var expected = new PdfPageSetup { Paper = size, Orientation = turned };
        (double width, double height) = await PageInchesAsync(workspace.At("doc.pdf"));

        width.ShouldBe(expected.WidthInches, 0.02);
        height.ShouldBe(expected.HeightInches, 0.02);
    }

    [Theory]
    [InlineData("normal", PageMargin.Normal)]
    [InlineData("narrow", PageMargin.Narrow)]
    [InlineData("moderate", PageMargin.Moderate)]
    [InlineData("wide", PageMargin.Wide)]
    [InlineData("none", PageMargin.None)]
    public async Task The_text_starts_at_the_margin_asked_for(string name, PageMargin margin)
    {
        using var workspace = new Workspace();
        workspace.Text("doc.md", Document);

        CliResult result = await CliRunner.RunAsync(["export", "pdf", "--margin", name, "doc.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());

        // A glyph's own side bearing puts its ink a hair inside the margin, never outside it.
        double left = await LeftInkInchesAsync(workspace.At("doc.pdf"));

        left.ShouldBeGreaterThanOrEqualTo(PageMargins.HorizontalInches(margin) - 0.01);
        left.ShouldBeLessThan(PageMargins.HorizontalInches(margin) + 0.1);
    }

    [Fact]
    public async Task A_paper_flag_outranks_the_paper_the_settings_name()
    {
        using var workspace = new Workspace();
        workspace.Text("doc.md", Document);
        workspace.Copy(Path.Combine("tests", "baselines", "cli", "preferences.json"), "letter.json");

        CliResult letter = await CliRunner.RunAsync(["export", "pdf", "--settings", "letter.json", "-o", "letter.pdf", "doc.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        CliResult a4 = await CliRunner.RunAsync(["export", "pdf", "--settings", "letter.json", "--paper", "A4", "-o", "a4.pdf", "doc.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        letter.ExitCode.ShouldBe(0, letter.ToString());
        a4.ExitCode.ShouldBe(0, a4.ToString());

        (await PageInchesAsync(workspace.At("letter.pdf"))).Width.ShouldBe(8.5, 0.02);
        (await PageInchesAsync(workspace.At("a4.pdf"))).Width.ShouldBe(8.27, 0.02);
    }

    [Fact]
    public async Task Backgrounds_on_and_off_together_are_a_usage_error()
    {
        CliResult result = await CliRunner.RunAsync(["export", "pdf", "--backgrounds", "--no-backgrounds", "doc.md"], renderers: new FakeRenderers().Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(2, result.ToString());
    }

    /// <summary>The first page's size in inches. Windows gives it in device-independent pixels, 96 to the inch.</summary>
    private static async Task<(double Width, double Height)> PageInchesAsync(string path)
    {
        PdfDocument pdf = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
        using PdfPage page = pdf.GetPage(0);

        return (page.Size.Width / 96, page.Size.Height / 96);
    }

    /// <summary>How far in from the page's left edge the first ink of the first page is, in inches.</summary>
    private static async Task<double> LeftInkInchesAsync(string path)
    {
        PdfDocument pdf = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
        using PdfPage page = pdf.GetPage(0);

        uint width = (uint)Math.Round(page.Size.Width / 96 * Dpi);
        using var stream = new InMemoryRandomAccessStream();

        await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = width });

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        PixelDataProvider pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        byte[] data = pixels.DetachPixelData();

        int columns = (int)decoder.PixelWidth;
        int rows = (int)decoder.PixelHeight;

        for (int x = 0; x < columns; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                int at = ((y * columns) + x) * 4;

                // Dark enough to be text, not the faint rule under a heading.
                if (data[at] < 128 && data[at + 1] < 128 && data[at + 2] < 128)
                {
                    return x / Dpi;
                }
            }
        }

        throw new InvalidOperationException($"No ink on the first page of {path}.");
    }
}
