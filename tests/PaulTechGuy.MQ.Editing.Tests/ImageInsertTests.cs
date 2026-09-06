// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Editing.Tests;

public class ImageInsertTests
{
    [Fact]
    public void An_image_goes_in_at_the_caret()
    {
        Edits.RunImages(Edits.Caret("see  here", 0, 4), "guide.assets/image-1.png")
            .ShouldBe("see ![](guide.assets/image-1.png) here");
    }

    [Fact]
    public void The_alt_text_is_left_empty_rather_than_carrying_a_placeholder()
    {
        // A placeholder word would be a non-empty alt text, so the check that looks for a
        // missing one would stay quiet and the document would ship labelled "alt".
        Edits.RunImages(Edits.Caret("", 0, 0), "shot.png").ShouldBe("![](shot.png)");
    }

    [Fact]
    public void The_caret_lands_between_the_alt_brackets()
    {
        // "![" is two characters, so the caret sits at column 2 ready for the alt text.
        Edits.CaretAfterImages(Edits.Caret("", 0, 0), "shot.png").ShouldBe(2);
    }

    [Fact]
    public void A_selection_becomes_the_alt_text()
    {
        // The one case where the author has already said what the picture is.
        Edits.RunImages(Edits.Selection("The login screen", 0, 0, 0, 16), "shot.png")
            .ShouldBe("![The login screen](shot.png)");
    }

    [Fact]
    public void The_caret_goes_after_the_alt_text_when_the_selection_supplied_it()
    {
        // Nothing left to type there, so the caret does not sit in the middle of a finished
        // label waiting to be moved.
        Edits.CaretAfterImages(Edits.Selection("Login", 0, 0, 0, 5), "shot.png").ShouldBe(7);
    }

    [Fact]
    public void Several_images_go_in_as_separate_blocks()
    {
        // More than one only happens when several files were copied together, and a run of
        // images on one line is not what anyone means by that.
        Edits.RunImages(Edits.Caret("", 0, 0), "a.png", "b.png")
            .ShouldBe("![](a.png)\n\n![](b.png)");
    }

    [Fact]
    public void Only_the_first_of_several_takes_the_selection_as_its_alt_text()
    {
        Edits.RunImages(Edits.Selection("Screens", 0, 0, 0, 7), "a.png", "b.png")
            .ShouldBe("![Screens](a.png)\n\n![](b.png)");
    }

    [Fact]
    public void A_reference_that_was_encoded_is_written_exactly_as_given()
    {
        // Encoding is the store's decision. Re-encoding or unescaping here would break it.
        Edits.RunImages(Edits.Caret("", 0, 0), "My%20Images/shot.png")
            .ShouldBe("![](My%20Images/shot.png)");
    }

    [Fact]
    public void Nothing_to_insert_changes_nothing()
    {
        Edits.RunImages(Edits.Caret("text", 0, 2)).ShouldBe("text");
    }

    [Fact]
    public void An_image_can_go_in_partway_through_an_existing_line()
    {
        Edits.RunImages(Edits.Caret("- item", 0, 6), "a.png")
            .ShouldBe("- item![](a.png)");
    }
}
