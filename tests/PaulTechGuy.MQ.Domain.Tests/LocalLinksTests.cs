// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

public class LocalLinksTests
{
    private static OutlineHeading Heading(string slug, int line) =>
        new() { Level = 2, Text = slug, Slug = slug, SourceLine = line };

    [Theory]
    [InlineData(@"C:\docs\setup.exe")]
    [InlineData(@"C:\docs\build.PS1")]
    [InlineData(@"C:\docs\notes.lnk")]
    [InlineData(@"C:\docs\install.msi")]
    [InlineData(@"C:\docs\tweak.reg")]
    public void A_program_is_runnable(string path) => LocalLinks.IsRunnable(path).ShouldBeTrue();

    [Theory]
    [InlineData(@"C:\docs\report.pdf")]
    [InlineData(@"C:\docs\budget.xlsx")]
    [InlineData(@"C:\docs\shot.png")]
    [InlineData(@"C:\docs\README")]
    public void A_document_is_not_runnable(string path) => LocalLinks.IsRunnable(path).ShouldBeFalse();

    [Theory]
    [InlineData(@"C:\docs\setup.exe.")]
    [InlineData(@"C:\docs\setup.exe . ")]
    public void Trailing_dots_and_spaces_do_not_hide_a_program(string path)
    {
        // Windows drops them when it opens the file, so "setup.exe." starts setup.exe.
        LocalLinks.IsRunnable(path).ShouldBeTrue();
    }

    [Fact]
    public void An_anchor_finds_its_heading()
    {
        OutlineHeading[] outline = [Heading("overview", 0), Heading("incident-history", 12)];

        LocalLinks.FindHeading(outline, "incident-history").ShouldNotBeNull().SourceLine.ShouldBe(12);
    }

    [Fact]
    public void An_exact_match_wins_over_one_that_differs_in_case()
    {
        OutlineHeading[] outline = [Heading("notes", 3), Heading("Notes", 9)];

        LocalLinks.FindHeading(outline, "Notes").ShouldNotBeNull().SourceLine.ShouldBe(9);
    }

    [Fact]
    public void Case_is_ignored_when_nothing_matches_exactly()
    {
        OutlineHeading[] outline = [Heading("incident-history", 12)];

        LocalLinks.FindHeading(outline, "Incident-History").ShouldNotBeNull().SourceLine.ShouldBe(12);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-such-heading")]
    public void No_match_is_null(string fragment) =>
        LocalLinks.FindHeading([Heading("overview", 0)], fragment).ShouldBeNull();
}
