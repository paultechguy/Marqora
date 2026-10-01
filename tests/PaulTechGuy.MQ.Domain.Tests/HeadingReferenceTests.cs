// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The link Insert Reference writes. The cases are the characters that would stop the link
/// being a link, or turn the heading's words into something they were not.
/// </summary>
public class HeadingReferenceTests
{
    [Fact]
    public void A_plain_heading_becomes_a_link_to_its_anchor()
    {
        HeadingReference.Link("Installing the app", "installing-the-app")
            .ShouldBe("[Installing the app](#installing-the-app)");
    }

    [Fact]
    public void A_number_is_written_as_it_reads()
    {
        HeadingReference.Link("2.3", "installing-the-app").ShouldBe("[2.3](#installing-the-app)");
    }

    /// <summary>An unescaped bracket ends the link text early and the reference is no longer a link.</summary>
    [Fact]
    public void Brackets_are_escaped()
    {
        HeadingReference.EscapeLabel("Arrays [and lists]").ShouldBe(@"Arrays \[and lists\]");
    }

    [Fact]
    public void A_backslash_is_escaped_rather_than_left_to_eat_the_next_character()
    {
        HeadingReference.EscapeLabel(@"C:\temp").ShouldBe(@"C:\\temp");
    }

    /// <summary>The heading was written with its asterisks escaped; the reference must not italicize them.</summary>
    [Fact]
    public void Emphasis_and_code_markers_are_escaped()
    {
        HeadingReference.EscapeLabel("The *real* `answer`").ShouldBe(@"The \*real\* \`answer\`");
    }

    [Fact]
    public void An_underscore_inside_a_word_is_left_alone()
    {
        HeadingReference.EscapeLabel("snake_case names").ShouldBe("snake_case names");
    }

    [Fact]
    public void An_underscore_at_a_word_edge_is_escaped()
    {
        HeadingReference.EscapeLabel("_private members").ShouldBe(@"\_private members");
    }

    [Fact]
    public void An_angle_bracket_is_escaped_so_it_cannot_open_a_tag()
    {
        HeadingReference.EscapeLabel("Use <kbd>").ShouldBe(@"Use \<kbd>");
    }
}
