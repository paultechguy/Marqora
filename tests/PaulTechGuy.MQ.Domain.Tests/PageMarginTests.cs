// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The margin presets, which both exports read from one place.
///
/// They did not always. There were two enums sharing four member names and agreeing on no
/// measurement at all - Normal was half an inch to the PDF export and a whole one to Word,
/// Wide an inch to one and two inches at the sides to the other - so the same document
/// exported both ways came out on two different measures under a single word, and a setup
/// seeded from one to the other kept the word while changing the page.
/// </summary>
public class PageMarginTests
{
    [Theory]
    [InlineData(PageMargin.Normal, 1.0, 1.0)]
    [InlineData(PageMargin.Narrow, 0.5, 0.5)]
    [InlineData(PageMargin.Moderate, 1.0, 0.75)]
    [InlineData(PageMargin.Wide, 1.0, 2.0)]
    [InlineData(PageMargin.None, 0.0, 0.0)]
    public void Every_preset_measures_what_Word_means_by_it(
        PageMargin margin,
        double vertical,
        double horizontal)
    {
        PageMargins.VerticalInches(margin).ShouldBe(vertical);
        PageMargins.HorizontalInches(margin).ShouldBe(horizontal);
    }

    [Fact]
    public void The_two_exports_agree_on_every_preset()
    {
        foreach (PageMargin margin in Enum.GetValues<PageMargin>())
        {
            var pdf = new PdfPageSetup { Margin = margin };
            var word = new DocxExportSetup { Margin = margin };

            word.VerticalMarginInches.ShouldBe(pdf.VerticalMarginInches, $"{margin} top");
            word.HorizontalMarginInches.ShouldBe(pdf.HorizontalMarginInches, $"{margin} sides");
        }
    }

    /// <summary>
    /// Every combo that offers these is built from the label list and read back as the index
    /// cast to the enum, so a label missing from the middle would not read as a missing row -
    /// it would silently shift every preset below it onto the wrong measurements.
    /// </summary>
    [Fact]
    public void There_is_exactly_one_label_for_each_preset()
    {
        PageMargins.Labels.Count.ShouldBe(Enum.GetValues<PageMargin>().Length);
        PageMargins.Labels.Distinct().Count().ShouldBe(PageMargins.Labels.Count);
    }

    /// <summary>
    /// Normal is the default because it is what Word gives a new document, and because a
    /// reader who never opens the dialog should get the ordinary thing.
    /// </summary>
    [Fact]
    public void Normal_is_an_inch_on_every_side_and_is_the_default()
    {
        PdfPageSetup.Default.Margin.ShouldBe(PageMargin.Normal);
        DocxExportSetup.Default.Margin.ShouldBe(PageMargin.Normal);

        PdfPageSetup.Default.HorizontalMarginInches.ShouldBe(1.0);
        PdfPageSetup.Default.VerticalMarginInches.ShouldBe(1.0);
    }
}
