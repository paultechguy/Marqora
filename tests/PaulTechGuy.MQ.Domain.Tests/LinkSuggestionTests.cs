// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

public class LinkSuggestionTests
{
    private static IReadOnlyList<string> For(string target, params string[] candidates) =>
        LinkSuggestions.For(target, candidates, limit: 3);

    [Fact]
    public void A_name_differing_only_in_case_comes_first()
    {
        // The commonest real miss on Windows: NTFS finds the file, the preview's ordinal
        // comparison does not, so the image is broken on screen while Explorer shows it there.
        For("logo.png", "art/diagram.png", "logo.PNG")
            .ShouldBe(["logo.PNG"]);
    }

    [Fact]
    public void The_whole_candidate_path_comes_back_not_just_the_name()
    {
        // Matching is on the file name, but a menu item has to write a path that resolves.
        For("logo.png", "images/logo.png").ShouldBe(["images/logo.png"]);
    }

    [Fact]
    public void The_same_name_with_a_different_extension_is_offered()
    {
        For("diagram.png", "diagram.svg").ShouldBe(["diagram.svg"]);
    }

    [Fact]
    public void A_typo_is_offered()
    {
        For("screenshot.png", "screenshto.png").ShouldBe(["screenshto.png"]);
    }

    [Fact]
    public void Case_beats_extension_and_extension_beats_a_typo()
    {
        // Order is the whole design: a wrong suggestion at the top gets clicked.
        For("logo.png", "logos.png", "logo.svg", "logo.PNG")
            .ShouldBe(["logo.PNG", "logo.svg", "logos.png"]);
    }

    [Fact]
    public void Something_unrelated_is_not_offered()
    {
        For("screenshot.png", "architecture.svg", "notes.md").ShouldBeEmpty();
    }

    [Fact]
    public void Two_short_unrelated_names_are_not_offered_for_each_other()
    {
        // "a.png" and "b.png" are one edit apart and have nothing to do with each other. The
        // guard is that a real typo does not change a name's length much.
        For("a.png", "index.png", "b.png").ShouldBe(["b.png"]);
    }

    [Fact]
    public void Extension_less_names_do_not_all_match_each_other()
    {
        // Both have an empty stem. Treating that as "same stem" would offer every one of them
        // for every other one.
        For("LICENSE", "CHANGELOG").ShouldBeEmpty();
    }

    [Fact]
    public void An_anchor_is_matched_whole_because_it_has_no_segments()
    {
        For("#getting-stated", "#getting-started", "#install")
            .ShouldBe(["#getting-started"]);
    }

    [Fact]
    public void No_more_than_the_limit_come_back()
    {
        LinkSuggestions.For("logo.png", ["logo.PNG", "logo.svg", "logo.gif", "logo.webp"], limit: 2)
            .Count.ShouldBe(2);
    }

    [Fact]
    public void The_same_name_in_two_folders_is_offered_twice_because_they_are_different_files()
    {
        For("logo.png", "art/logo.PNG", "images/logo.PNG").Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_to_go_on_produces_nothing(string target)
    {
        For(target, "logo.png").ShouldBeEmpty();
    }

    [Fact]
    public void An_empty_candidate_list_produces_nothing()
    {
        LinkSuggestions.For("logo.png", [], limit: 3).ShouldBeEmpty();
    }
}
