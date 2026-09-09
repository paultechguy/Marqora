// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folio.Tests;

/// <summary>
/// The planner, against the awkward folder the design document describes: images beside the
/// document, images above it, images that are not there, two images sharing a name, and one
/// reference sitting inside a fenced code block where nothing may touch it.
/// </summary>
public sealed class FolioPlannerTests
{
    [Fact]
    public void An_image_beside_the_document_keeps_the_path_the_author_wrote()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("docs/logo.png", "the-logo");
        workspace.Document("docs/guide.md", "![](logo.png)");

        FolioPlan plan = workspace.Plan();

        plan.Assets.ShouldHaveSingleItem();
        plan.Assets[0].EntryName.ShouldBe("logo.png");
        plan.Assets[0].Relocated.ShouldBeFalse();
        plan.Documents[0].Rewritten.ShouldBeFalse();
        plan.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void An_image_in_a_subfolder_keeps_its_relative_path()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("docs/guide.assets/shot.png", "a-screenshot");
        workspace.Document("docs/guide.md", "![](guide.assets/shot.png)");

        FolioPlan plan = workspace.Plan();

        plan.Assets[0].EntryName.ShouldBe("guide.assets/shot.png");
        plan.Assets[0].Relocated.ShouldBeFalse();
        FolioWorkspace.TextOf(plan, "guide.md").ShouldBe("![](guide.assets/shot.png)");
    }

    [Fact]
    public void An_image_referenced_by_absolute_path_is_collected_and_repointed()
    {
        using var workspace = new FolioWorkspace();

        string outside = workspace.File("elsewhere/chart.png", "a-chart");
        workspace.Document("docs/guide.md", $"![A chart]({outside.Replace('\\', '/')})");

        FolioPlan plan = workspace.Plan();

        plan.Assets.ShouldHaveSingleItem();
        plan.Assets[0].EntryName.ShouldBe("media/chart.png");
        plan.Assets[0].Relocated.ShouldBeTrue();
        FolioWorkspace.TextOf(plan, "guide.md").ShouldBe("![A chart](media/chart.png)");
    }

    [Fact]
    public void An_image_above_the_document_folder_is_collected_and_repointed()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("elsewhere/photo.png", "a-photo");
        workspace.Document("docs/guide.md", "![](../elsewhere/photo.png)");

        FolioPlan plan = workspace.Plan();

        plan.Assets[0].EntryName.ShouldBe("media/photo.png");
        plan.Assets[0].Relocated.ShouldBeTrue();
        FolioWorkspace.TextOf(plan, "guide.md").ShouldBe("![](media/photo.png)");
    }

    [Fact]
    public void A_missing_image_is_reported_and_nothing_is_invented_for_it()
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/guide.md", "![](diagrams/old-flow.png)");

        FolioPlan plan = workspace.Plan();

        plan.Assets.ShouldBeEmpty();
        plan.Documents[0].Rewritten.ShouldBeFalse();

        FolioWarning warning = plan.WarningsOf(FolioWarningKind.MissingImage).ShouldHaveSingleItem();
        warning.Url.ShouldBe("diagrams/old-flow.png");
        warning.Line.ShouldBe(0);
    }

    [Fact]
    public void The_same_image_used_by_two_documents_is_collected_once()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("docs/images/logo.png", "the-one-logo");
        workspace.Document("docs/one.md", "![](images/logo.png)");
        workspace.Document("docs/two.md", "![](images/logo.png)");

        FolioPlan plan = workspace.Plan();

        plan.Assets.ShouldHaveSingleItem();
        plan.Assets[0].EntryName.ShouldBe("images/logo.png");
    }

    [Fact]
    public void The_same_bytes_under_two_names_are_collected_once_and_both_references_agree()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("docs/logo.png", "identical-bytes");
        workspace.File("elsewhere/logo-copy.png", "identical-bytes");
        workspace.Document("docs/guide.md", "![](logo.png) and ![](../elsewhere/logo-copy.png)");

        FolioPlan plan = workspace.Plan();

        plan.Assets.ShouldHaveSingleItem();
        FolioWorkspace.TextOf(plan, "guide.md").ShouldBe("![](logo.png) and ![](logo.png)");
    }

    [Fact]
    public void Two_different_images_sharing_a_name_do_not_overwrite_each_other()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("docs/chart.png", "first-chart");
        workspace.File("elsewhere/chart.png", "second-chart-different-bytes");
        workspace.Document("docs/guide.md", "![](chart.png) then ![](../elsewhere/chart.png)");

        FolioPlan plan = workspace.Plan();

        plan.Assets.Count.ShouldBe(2);
        plan.Assets[0].EntryName.ShouldBe("chart.png");
        plan.Assets[1].EntryName.ShouldBe("media/chart.png");
        FolioWorkspace.TextOf(plan, "guide.md").ShouldBe("![](chart.png) then ![](media/chart.png)");
    }

    /// <summary>
    /// The reason references are repointed by position. A whole-text replace would rewrite the
    /// sample inside the fence as well, which is how a document about markdown gets corrupted.
    /// </summary>
    [Fact]
    public void A_reference_inside_a_fenced_code_block_is_never_rewritten()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("elsewhere/chart.png", "a-chart");
        string reference = $"![](../elsewhere/chart.png)";

        workspace.Document(
            "docs/guide.md",
            $"""
             {reference}

             ```markdown
             {reference}
             ```
             """);

        string text = FolioWorkspace.TextOf(workspace.Plan(), "guide.md");

        text.ShouldBe(
            """
            ![](media/chart.png)

            ```markdown
            ![](../elsewhere/chart.png)
            ```
            """);
    }

    [Fact]
    public void A_link_between_two_documents_in_the_folio_is_flattened_to_the_new_layout()
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/guide.md", "See [setup](manual/setup.md).");
        workspace.Document("docs/manual/setup.md", "Setup.");

        FolioPlan plan = workspace.Plan();

        FolioWorkspace.TextOf(plan, "guide.md").ShouldBe("See [setup](setup.md).");
        plan.WarningsOf(FolioWarningKind.OutsideLink).ShouldBeEmpty();
    }

    [Fact]
    public void A_fragment_survives_a_link_being_repointed()
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/guide.md", "See [setup](manual/setup.md#install).");
        workspace.Document("docs/manual/setup.md", "# Install");

        FolioWorkspace.TextOf(workspace.Plan(), "guide.md").ShouldBe("See [setup](setup.md#install).");
    }

    [Fact]
    public void A_link_leaving_the_folio_is_reported_rather_than_rewritten()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("elsewhere/scratch.md", "notes");
        workspace.Document("docs/guide.md", "See [notes](../elsewhere/scratch.md).");

        FolioPlan plan = workspace.Plan();

        plan.WarningsOf(FolioWarningKind.OutsideLink).ShouldHaveSingleItem()
            .Url.ShouldBe("../elsewhere/scratch.md");
        plan.Documents[0].Rewritten.ShouldBeFalse();
    }

    [Fact]
    public void An_external_url_is_left_entirely_alone()
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/guide.md", "[home](https://example.com) and ![](http://x/y.png)");

        FolioPlan plan = workspace.Plan();

        plan.Assets.ShouldBeEmpty();
        plan.Warnings.ShouldBeEmpty();
        plan.Documents[0].Rewritten.ShouldBeFalse();
    }

    [Fact]
    public void Two_documents_with_the_same_name_get_distinct_entries()
    {
        using var workspace = new FolioWorkspace();

        workspace.Document("docs/notes.md", "one");
        workspace.Document("docs/archive/notes.md", "two");

        FolioPlan plan = workspace.Plan();

        plan.Documents.Select(d => d.EntryName).ShouldBe(["notes.md", "notes-1.md"]);
    }

    [Fact]
    public void A_rewrite_lands_correctly_on_a_later_line_with_windows_line_endings()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("elsewhere/chart.png", "a-chart");
        workspace.Document(
            "docs/guide.md",
            "# Title\r\n\r\nSome prose here.\r\n\r\n![](../elsewhere/chart.png)\r\n");

        FolioWorkspace.TextOf(workspace.Plan(), "guide.md")
            .ShouldBe("# Title\r\n\r\nSome prose here.\r\n\r\n![](media/chart.png)\r\n");
    }

    [Fact]
    public void Several_rewrites_on_one_line_all_land()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("elsewhere/a.png", "aaa");
        workspace.File("elsewhere/b.png", "bbb");
        workspace.Document("docs/guide.md", "![](../elsewhere/a.png) ![](../elsewhere/b.png)");

        FolioWorkspace.TextOf(workspace.Plan(), "guide.md").ShouldBe("![](media/a.png) ![](media/b.png)");
    }

    [Fact]
    public void Totals_count_what_travels()
    {
        using var workspace = new FolioWorkspace();

        workspace.File("docs/logo.png", "12345");
        workspace.File("elsewhere/chart.png", "1234567890");
        workspace.Document("docs/guide.md", "![](logo.png) ![](../elsewhere/chart.png)");

        FolioPlan plan = workspace.Plan();

        plan.KeptCount.ShouldBe(1);
        plan.RelocatedCount.ShouldBe(1);
        plan.TotalBytes.ShouldBeGreaterThan(15);
    }

    [Fact]
    public void An_empty_set_plans_to_nothing()
    {
        FolioPlanner.Plan([]).ShouldBeSameAs(FolioPlan.Empty);
    }
}
