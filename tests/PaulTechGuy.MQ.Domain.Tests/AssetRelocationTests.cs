// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

public class AssetRelocationTests
{
    private const string Guide = @"C:\docs\guide.md";

    private static IReadOnlyList<AssetMove> Plan(
        string to,
        string text,
        ImageFolderMode mode = ImageFolderMode.DocumentAssets) =>
        AssetRelocation.Plan(Guide, to, text, mode);

    [Fact]
    public void Renaming_within_the_folder_renames_the_asset_folder_with_it()
    {
        // The links would still resolve, but "guide.assets" beside a document called manual.md
        // is a folder named after something that no longer exists.
        AssetMove move = Plan(@"C:\docs\manual.md", "![](guide.assets/shot.png)").ShouldHaveSingleItem();

        move.NewReference.ShouldBe("manual.assets/shot.png");
        move.SourcePath.ShouldBe(@"C:\docs\guide.assets\shot.png");
        move.TargetPath.ShouldBe(@"C:\docs\manual.assets\shot.png");
    }

    [Fact]
    public void Moving_to_another_folder_takes_the_images_along()
    {
        // Here the links break outright, which is the case that actually loses pictures.
        AssetMove move = Plan(@"D:\other\guide.md", "![](guide.assets/shot.png)").ShouldHaveSingleItem();

        move.NewReference.ShouldBe("guide.assets/shot.png");
        move.TargetPath.ShouldBe(@"D:\other\guide.assets\shot.png");
    }

    [Fact]
    public void Saving_over_the_same_file_moves_nothing()
    {
        Plan(Guide, "![](guide.assets/shot.png)").ShouldBeEmpty();
    }

    [Fact]
    public void A_document_with_no_images_has_nothing_to_carry()
    {
        Plan(@"D:\other\guide.md", "# Guide\n\nJust prose.").ShouldBeEmpty();
    }

    [Fact]
    public void Each_image_is_planned_once_however_often_it_appears()
    {
        Plan(@"D:\other\guide.md", "![a](guide.assets/x.png) and again ![b](guide.assets/x.png)")
            .Count.ShouldBe(1);
    }

    // ------------------------------------------------------- what is not ours to move

    [Fact]
    public void An_external_image_is_left_alone()
    {
        Plan(@"D:\other\guide.md", "![](https://example.com/logo.png)").ShouldBeEmpty();
    }

    [Fact]
    public void An_image_outside_the_documents_folder_is_left_alone()
    {
        // Reaching up and out means it belongs to something else. Copying it would give the
        // project two of them and leave whoever else uses it none the wiser.
        Plan(@"D:\other\guide.md", "![](../shared/logo.png)").ShouldBeEmpty();
    }

    [Fact]
    public void Another_documents_asset_folder_is_left_alone()
    {
        Plan(@"D:\other\guide.md", "![](notes.assets/shot.png)").ShouldBeEmpty();
    }

    [Fact]
    public void A_shared_images_folder_is_carried_but_not_renamed()
    {
        AssetMove move = Plan(@"D:\other\guide.md", "![](images/shot.png)", ImageFolderMode.SharedImages)
            .ShouldHaveSingleItem();

        move.NewReference.ShouldBe("images/shot.png");
        move.TargetPath.ShouldBe(@"D:\other\images\shot.png");
    }

    [Fact]
    public void A_shared_folder_does_not_move_when_the_document_only_changes_name()
    {
        // Nothing about "images" depends on what the document is called, and the other documents
        // beside it still need it where it is.
        Plan(@"C:\docs\manual.md", "![](images/shot.png)", ImageFolderMode.SharedImages)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Beside_the_document_only_image_files_are_carried()
    {
        IReadOnlyList<AssetMove> moves = Plan(
            @"D:\other\guide.md",
            "![](shot.png) and [notes](notes.md)",
            ImageFolderMode.BesideDocument);

        // A sibling markdown file is a different document, not this one's asset.
        moves.ShouldHaveSingleItem().OldReference.ShouldBe("shot.png");
    }

    // ------------------------------------------------------------------- rewriting

    [Fact]
    public void Rewriting_repoints_only_the_target()
    {
        IReadOnlyList<AssetMove> moves = Plan(@"C:\docs\manual.md", "![the logo](guide.assets/shot.png)");

        // The alt text the author wrote is theirs and must survive.
        AssetRelocation.Rewrite("![the logo](guide.assets/shot.png)", moves)
            .ShouldBe("![the logo](manual.assets/shot.png)");
    }

    [Fact]
    public void Rewriting_keeps_a_title_after_the_target()
    {
        IReadOnlyList<AssetMove> moves = Plan(@"C:\docs\manual.md", "![](guide.assets/shot.png \"Figure 1\")");

        AssetRelocation.Rewrite("![](guide.assets/shot.png \"Figure 1\")", moves)
            .ShouldBe("![](manual.assets/shot.png \"Figure 1\")");
    }

    [Fact]
    public void Rewriting_nothing_changes_nothing()
    {
        const string text = "![](guide.assets/shot.png)";

        AssetRelocation.Rewrite(text, []).ShouldBe(text);
    }

    [Fact]
    public void An_encoded_reference_survives_the_round_trip()
    {
        IReadOnlyList<AssetMove> moves = AssetRelocation.Plan(
            @"C:\docs\My Guide.md",
            @"C:\docs\My Manual.md",
            "![](my-guide.assets/shot.png)",
            ImageFolderMode.DocumentAssets);

        moves.ShouldHaveSingleItem().NewReference.ShouldBe("my-manual.assets/shot.png");
    }
}
