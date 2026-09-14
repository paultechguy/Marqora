// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folio.Tests;

/// <summary>
/// Pictures named by a web address: which references count as one, what happens when nothing is
/// fetched, and what happens when something was.
///
/// Several of these guard mistakes that were made on paper and caught before they were written.
/// They are named for what they protect rather than for what they do, because the failure they
/// describe is plausible enough to be reintroduced by somebody tidying up.
///
/// Nothing here touches a network. The fetcher is tested separately, against a listener on
/// loopback; the planner is handed a map and knows nothing about where it came from.
/// </summary>
public sealed class FolioRemoteImageTests
{
    [Fact]
    public void Ordinary_links_are_not_counted_as_pictures_on_the_web()
    {
        // The test that exists because the check sits above the one telling images from links.
        // Without that guard a README of reference links announces thirty pictures on the web,
        // and the first person to see it switches the feature off.
        using var workspace = new FolioWorkspace();

        string links = string.Join(
            "\n\n",
            Enumerable.Range(1, 30).Select(i => $"[reference {i}](https://example.com/page/{i})"));

        workspace.Document("docs/guide.md", links + "\n\n![A badge](https://img.shields.io/x.svg)");

        FolioPlan plan = workspace.Plan();

        plan.RemoteImages.ShouldHaveSingleItem();
        plan.RemoteImages[0].Url.ShouldBe("https://img.shields.io/x.svg");
        plan.RemoteHosts.ShouldBe(["img.shields.io"]);
    }

    [Fact]
    public void A_badge_inside_a_link_is_the_picture_not_the_link()
    {
        // "[![](badge.svg)](https://ci)" puts an image and a link on one line, both of them
        // carrying a web address, and only one of them is a picture.
        using var workspace = new FolioWorkspace();

        workspace.Document(
            "docs/guide.md",
            "[![build](https://img.shields.io/build.svg)](https://ci.example.com/job)");

        FolioPlan plan = workspace.Plan();

        plan.RemoteImages.ShouldHaveSingleItem();
        plan.RemoteImages[0].Url.ShouldBe("https://img.shields.io/build.svg");
    }

    [Theory]
    [InlineData("![](data:image/png;base64,iVBORw0KGgo=)")]
    [InlineData("![](mailto:someone@example.com)")]
    [InlineData("![](file:///C:/pictures/chart.png)")]
    public void A_scheme_that_is_not_the_web_is_not_a_picture_on_the_web(string markdown)
    {
        // Scheme() matches every one of these. A data: picture is already inside the document,
        // so offering to go and fetch it would be nonsense - and MediaTarget is what draws that
        // line, once, for the whole application.
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/guide.md", markdown);

        workspace.Plan().RemoteImages.ShouldBeEmpty();
    }

    [Fact]
    public void A_remote_picture_that_is_not_fetched_is_reported_and_left_exactly_as_written()
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/guide.md", "![A chart](https://example.com/chart.png)");

        FolioPlan plan = workspace.Plan();

        plan.Assets.ShouldBeEmpty();
        plan.Documents[0].Rewritten.ShouldBeFalse();
        FolioWorkspace.TextOf(plan, "guide.md").ShouldBe("![A chart](https://example.com/chart.png)");

        FolioWarning warning = plan.Warnings.ShouldHaveSingleItem();
        warning.Kind.ShouldBe(FolioWarningKind.RemoteImageNotIncluded);
        warning.Url.ShouldBe("https://example.com/chart.png");
    }

    [Fact]
    public void A_fetched_picture_is_collected_and_repointed()
    {
        using var workspace = new FolioWorkspace();

        string bytes = workspace.File("elsewhere/chart.png", "a-chart");
        workspace.Document("docs/guide.md", "![A chart](https://example.com/chart.png)");

        FolioPlan plan = workspace.Plan(
            new Dictionary<string, string> { ["https://example.com/chart.png"] = bytes });

        FolioAsset asset = plan.Assets.ShouldHaveSingleItem();
        asset.EntryName.ShouldBe("media/chart.png");
        asset.Relocated.ShouldBeTrue();
        asset.RemoteUrl.ShouldBe("https://example.com/chart.png");
        plan.FetchedCount.ShouldBe(1);

        FolioWorkspace.TextOf(plan, "guide.md").ShouldBe("![A chart](media/chart.png)");
        plan.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void A_picture_that_failed_to_fetch_is_left_exactly_as_written()
    {
        // A failed fetch is simply absent from the map, which is what the planner has always
        // done with a web address. No separate path, and so nothing that can rot on its own.
        using var workspace = new FolioWorkspace();

        string bytes = workspace.File("elsewhere/other.png", "something-else");
        workspace.Document(
            "docs/guide.md",
            "![Came back](https://example.com/ok.png)\n\n![Did not](https://example.com/gone.png)");

        FolioPlan plan = workspace.Plan(
            new Dictionary<string, string> { ["https://example.com/ok.png"] = bytes });

        plan.Assets.ShouldHaveSingleItem();
        FolioWorkspace.TextOf(plan, "guide.md")
            .ShouldContain("![Did not](https://example.com/gone.png)");
    }

    [Fact]
    public void A_query_string_is_part_of_the_address_and_survives()
    {
        // A badge carries its meaning in the query, so ".../build?style=flat" and ".../build"
        // are different pictures. Cutting at the "?" would fetch one and show the other.
        using var workspace = new FolioWorkspace();

        string flat = workspace.File("elsewhere/flat.svg", "the-flat-one");
        workspace.Document("docs/guide.md", "![](https://img.shields.io/build?style=flat)");

        FolioPlan plan = workspace.Plan(
            new Dictionary<string, string> { ["https://img.shields.io/build?style=flat"] = flat });

        plan.RemoteImages[0].Url.ShouldBe("https://img.shields.io/build?style=flat");
        plan.Assets.ShouldHaveSingleItem();
        FolioWorkspace.TextOf(plan, "guide.md").ShouldBe("![](media/flat.svg)");
    }

    [Fact]
    public void A_remote_picture_written_as_a_raw_html_tag_is_repointed()
    {
        // For a raw tag the reported span covers the address itself rather than "](url)", so
        // the ordinary repoint lands correctly. The natural assumption is that it cannot, which
        // is exactly why this is written down.
        using var workspace = new FolioWorkspace();

        string bytes = workspace.File("elsewhere/wide.png", "a-wide-picture");
        workspace.Document("docs/guide.md", "<img src=\"https://example.com/wide.png\" width=\"400\">");

        FolioPlan plan = workspace.Plan(
            new Dictionary<string, string> { ["https://example.com/wide.png"] = bytes });

        plan.Assets.ShouldHaveSingleItem();
        FolioWorkspace.TextOf(plan, "guide.md")
            .ShouldBe("<img src=\"media/wide.png\" width=\"400\">");
    }

    [Fact]
    public void The_same_remote_picture_in_two_documents_is_collected_once()
    {
        using var workspace = new FolioWorkspace();

        string bytes = workspace.File("elsewhere/logo.png", "one-logo");

        workspace.Document("docs/one.md", "![](https://example.com/logo.png)");
        workspace.Document("docs/two.md", "![](https://example.com/logo.png)");

        FolioPlan plan = workspace.Plan(
            new Dictionary<string, string> { ["https://example.com/logo.png"] = bytes });

        plan.Assets.ShouldHaveSingleItem();
        plan.RemoteImages.Count.ShouldBe(2);
        plan.RemoteHosts.ShouldHaveSingleItem();
        FolioWorkspace.TextOf(plan, "two.md").ShouldBe("![](media/logo.png)");
    }

    [Fact]
    public void An_iframe_is_reported_but_never_offered_for_fetching()
    {
        // The case that exposed this: an iframe is something the reader's browser loads, so it
        // is worth saying - but it is a live page, not a picture, and offering to download it
        // only produced "what came back was not a picture" about a perfectly good web page.
        using var workspace = new FolioWorkspace();

        workspace.Document(
            "docs/guide.md",
            "<iframe src=\"https://example.com\" width=\"200\" title=\"t\"></iframe>");

        FolioPlan plan = workspace.Plan();

        plan.RemoteImages.ShouldBeEmpty();

        FolioWarning warning = plan.Warnings.ShouldHaveSingleItem();
        warning.Kind.ShouldBe(FolioWarningKind.RemoteMediaNotIncluded);
        warning.Url.ShouldBe("https://example.com");
    }

    [Theory]
    [InlineData("<video src=\"https://example.com/film.mp4\"></video>")]
    [InlineData("<audio src=\"https://example.com/track.mp3\"></audio>")]
    [InlineData("<embed src=\"https://example.com/thing\">")]
    [InlineData("<object data=\"https://example.com/thing\"></object>")]
    public void Timed_media_and_frames_are_never_offered_for_fetching(string markup)
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/guide.md", markup);

        FolioPlan plan = workspace.Plan();

        plan.RemoteImages.ShouldBeEmpty();
        plan.Warnings.ShouldHaveSingleItem().Kind.ShouldBe(FolioWarningKind.RemoteMediaNotIncluded);
    }

    [Fact]
    public void A_video_poster_is_a_picture_even_though_its_video_is_not()
    {
        // The tag alone cannot answer this: "poster" is a still picture while "src" beside it is
        // a film, and treating them alike is how a Folio ends up offering to download a movie.
        using var workspace = new FolioWorkspace();

        workspace.Document(
            "docs/guide.md",
            "<video src=\"https://example.com/film.mp4\" poster=\"https://example.com/still.png\"></video>");

        FolioPlan plan = workspace.Plan();

        plan.RemoteImages.ShouldHaveSingleItem().Url.ShouldBe("https://example.com/still.png");

        // Two warnings, and they are different things. The film can never travel; the poster
        // could have, and is only being left behind because nobody asked for it.
        plan.WarningsOf(FolioWarningKind.RemoteMediaNotIncluded)
            .ShouldHaveSingleItem().Url.ShouldBe("https://example.com/film.mp4");

        plan.WarningsOf(FolioWarningKind.RemoteImageNotIncluded)
            .ShouldHaveSingleItem().Url.ShouldBe("https://example.com/still.png");
    }

    [Fact]
    public void A_picture_that_failed_to_fetch_is_reported_once_not_twice()
    {
        // Two rows for one picture: the fetcher said it could not be fetched, and the planner
        // said it was left on the web, because an address that failed is missing from the map
        // in exactly the way an address nobody asked for is. A non-null map is the difference,
        // and the planner can see it.
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/guide.md", "![](https://example.com/gone.png)");

        FolioPlan attempted = workspace.Plan(new Dictionary<string, string>());

        attempted.Warnings.ShouldBeEmpty();

        // And with no fetch attempted at all, it is still said - that is the whole point of it.
        workspace.Plan().Warnings.ShouldHaveSingleItem()
            .Kind.ShouldBe(FolioWarningKind.RemoteImageNotIncluded);
    }

    [Fact]
    public void A_fetched_picture_is_shrunk_like_any_other()
    {
        // Proves the ordering the build relies on. Fetch, then plan, then shrink: by the time the
        // shrinker runs a fetched picture is an ordinary asset with its width already read off the
        // bytes, so an oversized one from the web is reduced rather than slipping past the cap.
        // Shrinking first would leave these unmeasured and unreduced.
        using var workspace = new FolioWorkspace();

        string wide = Path.Combine(workspace.Elsewhere, "wide.png");
        File.WriteAllBytes(wide, Png(2000, 100));

        workspace.Document("docs/guide.md", "![](https://example.com/wide.png)");

        FolioPlan plan = FolioPlanner.Plan(
            workspace.Sources,
            maxImageWidth: 800,
            fetched: new Dictionary<string, string> { ["https://example.com/wide.png"] = wide });

        plan.Assets.ShouldHaveSingleItem().PixelWidth.ShouldBe(2000u);
        plan.Warnings.ShouldHaveSingleItem().Kind.ShouldBe(FolioWarningKind.WillBeShrunk);
    }

    /// <summary>A PNG header carrying real dimensions, so the width really is read off the bytes.</summary>
    private static byte[] Png(uint width, uint height) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        (byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width,
        (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89,
    ];

    [Fact]
    public void A_protocol_relative_address_is_on_the_web_and_names_its_host()
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/guide.md", "![](//cdn.example.com/logo.png)");

        FolioPlan plan = workspace.Plan();

        plan.RemoteImages.ShouldHaveSingleItem();
        plan.RemoteHosts.ShouldBe(["cdn.example.com"]);
    }
}
