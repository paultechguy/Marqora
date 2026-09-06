// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Analysis.Tests;

public class ImageCheckTests
{
    private static LinkFinding? AltTextFindingIn(string markdown) =>
        DocumentFolder.LinksUnsaved(markdown).FirstOrDefault(f => f.Kind == LinkFindingKind.MissingAltText);

    [Fact]
    public void Nothing_about_an_image_ever_becomes_a_marker()
    {
        // The whole reason this rule is a LinkFinding. A Diagnostic becomes a Monaco marker, and
        // a marker drags "View Problem" and "No quick fixes available" into the hover - for
        // every finding sharing that line, not just its own. One marker here undid the decision
        // to move dead links off markers in the first place.
        DocumentFolder.CheckUnsaved("![](../docs2/logo.png)").ShouldBeEmpty();

        // ...while the finding itself is still made, on the other channel.
        DocumentFolder.LinksUnsaved("![](../docs2/logo.png)")
            .ShouldContain(f => f.Kind == LinkFindingKind.MissingAltText);
    }

    [Fact]
    public void An_image_with_no_alt_text_is_reported()
    {
        LinkFinding found = AltTextFindingIn("![](logo.png)").ShouldNotBeNull();

        found.Message.ShouldBe("This image has no alt text.");
        found.Kind.ShouldBe(LinkFindingKind.MissingAltText);
    }

    [Fact]
    public void An_image_with_alt_text_is_not_reported()
    {
        AltTextFindingIn("![the company logo](logo.png)").ShouldBeNull();
    }

    [Fact]
    public void Alt_text_made_of_several_inlines_still_counts()
    {
        // "![the **new** logo](x)" is three inlines to the parser and one label to a reader.
        AltTextFindingIn("![the **new** logo](logo.png)").ShouldBeNull();
    }

    [Fact]
    public void A_deliberately_blank_alt_text_is_an_answer_not_an_omission()
    {
        // A space between the brackets is how the accessibility guidance says to mark an image
        // as decorative. Reporting it would be arguing with someone who already did the work.
        AltTextFindingIn("![ ](divider.png)").ShouldBeNull();
    }

    [Fact]
    public void A_badge_wrapped_in_a_link_is_not_reported()
    {
        // The link carries the accessible name, so the image inside it is decorative. Without
        // this, a README of shields lights up on every line.
        AltTextFindingIn("[![](https://img.shields.io/badge/build-passing.svg)](https://ci.example)")
            .ShouldBeNull();
    }

    [Fact]
    public void An_image_next_to_a_link_is_still_reported()
    {
        // Adjacent, not nested - nothing is carrying a name for it.
        AltTextFindingIn("[docs](README.md) and ![](logo.png)").ShouldNotBeNull();
    }

    [Fact]
    public void A_link_with_no_text_is_not_an_image_and_is_left_alone()
    {
        AltTextFindingIn("[](README.md)").ShouldBeNull();
    }

    [Fact]
    public void It_works_on_a_document_that_has_never_been_saved()
    {
        // The point of the rule living apart from the link checks: whether an image says what
        // it is has nothing to do with where the document lives, so this is the one image rule
        // that survives having no folder.
        AltTextFindingIn("![](logo.png)").ShouldNotBeNull();
    }

    [Fact]
    public void The_preference_turns_it_off_without_touching_anything_else()
    {
        DocumentFolder.LinksUnsaved("![](logo.png)   ", altText: false)
            .ShouldNotContain(f => f.Kind == LinkFindingKind.MissingAltText);

        // The trailing whitespace on that line is still reported, so only the one rule went and
        // the style checks beside it are untouched.
        DocumentFolder.CheckUnsaved("![](logo.png)   ", altText: false)
            .ShouldContain(d => d.Rule == "trailing-whitespace");
    }

    [Fact]
    public void The_underline_covers_the_whole_reference()
    {
        LinkFinding found = AltTextFindingIn("see ![](logo.png) here").ShouldNotBeNull();

        found.Start.ShouldBe(4);
        found.Length.ShouldBe("![](logo.png)".Length);
    }
}
