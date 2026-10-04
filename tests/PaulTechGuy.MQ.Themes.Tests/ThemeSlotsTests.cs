// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Themes.Tests;

/// <summary>
/// The slot list itself, and the one thing outside it that names slots: the webshell's
/// stylesheets.
/// </summary>
public sealed partial class ThemeSlotsTests
{
    [Fact]
    public void Slot_ids_are_unique() =>
        ThemeSlots.All.Select(s => s.Id).ShouldBeUnique();

    /// <summary>An id becomes a CSS custom property name as it stands, so it is kebab-case and nothing else.</summary>
    [Fact]
    public void Slot_ids_are_lowercase_kebab_case() =>
        ThemeSlots.All.Where(s => !KebabCase().IsMatch(s.Id)).Select(s => s.Id).ShouldBeEmpty();

    [Fact]
    public void Every_slot_says_what_it_paints() =>
        ThemeSlots.All.Where(s => string.IsNullOrWhiteSpace(s.Meaning)).Select(s => s.Id).ShouldBeEmpty();

    /// <summary>
    /// A rule names a real slot or one of the two neutrals - and never a diagram slot from
    /// outside the diagrams, which a stock-diagram theme does not carry.
    /// </summary>
    [Fact]
    public void Every_contrast_rule_reads_against_something_that_exists()
    {
        foreach (ThemeSlot slot in ThemeSlots.All.Where(s => s.Contrast is not null))
        {
            string against = slot.Contrast!.Against;

            if (against is ThemeSlots.Page or ThemeSlots.BodyText)
            {
                continue;
            }

            ThemeSlot? target = ThemeSlots.Find(against);

            target.ShouldNotBeNull($"{slot.Id} reads against {against}, which is not a slot.");

            if (target.Group == ThemeSlots.DiagramsGroup)
            {
                slot.Group.ShouldBe(ThemeSlots.DiagramsGroup, $"{slot.Id} reads against a diagram slot.");
            }
        }
    }

    /// <summary>
    /// Nothing in the webshell's CSS reads a slot that is not on the list. A misspelled one
    /// would not fail anywhere else - an undefined custom property quietly inherits, and the
    /// element simply comes out the wrong color.
    /// </summary>
    [Fact]
    public void The_stylesheets_read_only_slots_that_exist()
    {
        List<string> unknown = [];

        foreach (string path in Directory.EnumerateFiles(Path.Combine(Repository.Root(), "webshell"), "*.css"))
        {
            foreach (Match match in ThemeVariable().Matches(File.ReadAllText(path)))
            {
                string id = match.Groups["id"].Value;

                if (!ThemeSlots.IsSlot(id))
                {
                    unknown.Add($"{Path.GetFileName(path)}: --mq-theme-{id}");
                }
            }
        }

        unknown.ShouldBeEmpty();
    }

    [Fact]
    public void A_stock_diagram_theme_is_required_to_name_everything_but_the_diagrams()
    {
        ThemeSlots.RequiredFor(stockDiagrams: true).ShouldNotContain(s => s.Group == ThemeSlots.DiagramsGroup);
        ThemeSlots.RequiredFor(stockDiagrams: false).Count().ShouldBe(ThemeSlots.All.Count);
    }

    [Fact]
    public void Contrast_matches_the_published_ratios()
    {
        Contrast.Ratio("#000000", "#ffffff").ShouldBe(21, tolerance: 0.001);
        Contrast.Ratio("#ffffff", "#ffffff").ShouldBe(1, tolerance: 0.001);

        // Marqora's teal on white: the reading that put Default's link through AA.
        Contrast.Ratio("#3f8f98", "#ffffff").ShouldBe(3.75, tolerance: 0.01);
        Contrast.Ratio("#ffffff", "#3f8f98").ShouldBe(Contrast.Ratio("#3f8f98", "#ffffff"));
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex KebabCase();

    [GeneratedRegex(@"--mq-theme-(?<id>[a-z0-9-]+)")]
    private static partial Regex ThemeVariable();
}
