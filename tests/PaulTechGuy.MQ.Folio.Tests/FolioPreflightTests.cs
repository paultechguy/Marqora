// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folio.Tests;

/// <summary>
/// The preflight's figures and advice, moved out of the app's Folio window so marqora-cli reads
/// the same ones: what a set weighs in each form, which pictures shrinking would reduce, and when
/// one page is the wrong shape.
/// </summary>
public sealed class FolioPreflightTests
{
    private const long Megabyte = 1024 * 1024;

    private static FolioPlan Plan(params (long Bytes, uint Width)[] assets) => new()
    {
        Documents = [],
        Warnings = [],
        Assets = [.. assets.Select((a, i) => new FolioAsset
        {
            SourcePath = $"C:\\pics\\{i}.png",
            EntryName = $"media/{i}.png",
            Bytes = a.Bytes,
            PixelWidth = a.Width,
            Relocated = true,
        })],
    };

    [Fact]
    public void One_page_weighs_a_third_more_than_its_parts_because_its_pictures_are_base64()
    {
        FolioPlan plan = Plan((3 * Megabyte, 800));

        FolioPreflight.Weight(plan, singleFile: false).ShouldBe(plan.TotalBytes);
        FolioPreflight.Weight(plan, singleFile: true).ShouldBe(plan.TotalBytes * 4 / 3);
    }

    [Fact]
    public void A_single_page_past_the_heavy_limit_is_warned_about_and_offered_the_zip()
    {
        FolioAdvice? advice = FolioPreflight.Advice(Plan((40 * Megabyte, 800)), singleFile: true, maxImageWidth: 0);

        advice.ShouldNotBeNull();
        advice.Level.ShouldBe(FolioAdviceLevel.Warning);
        advice.Title.ShouldBe("This will be a big file");
        advice.Message.ShouldContain("A zip of the same documents would be about 40.0 MB");
    }

    [Fact]
    public void Past_the_mail_limit_is_information_not_a_warning()
    {
        FolioAdvice? advice = FolioPreflight.Advice(Plan((20 * Megabyte, 800)), singleFile: true, maxImageWidth: 0);

        advice.ShouldNotBeNull();
        advice.Level.ShouldBe(FolioAdviceLevel.Information);
    }

    [Fact]
    public void The_same_set_as_a_zip_gets_no_advice_about_one_page()
    {
        FolioPreflight.Advice(Plan((40 * Megabyte, 800)), singleFile: false, maxImageWidth: 0).ShouldBeNull();
    }

    [Fact]
    public void Oversized_pictures_are_offered_for_shrinking_only_while_shrinking_is_off()
    {
        FolioPlan plan = Plan((Megabyte, 3000), (Megabyte, 800));

        FolioPreflight.Oversized(plan, maxImageWidth: 0).Count.ShouldBe(1);
        FolioPreflight.Oversized(plan, maxImageWidth: 1600).ShouldBeEmpty();
    }

    [Fact]
    public void A_set_mostly_made_of_reducible_pictures_is_told_so_in_any_form()
    {
        FolioAdvice? advice = FolioPreflight.Advice(Plan((8 * Megabyte, 4000)), singleFile: false, maxImageWidth: 0);

        advice.ShouldNotBeNull();
        advice.Title.ShouldBe("Some images are larger than they need to be");
    }
}
