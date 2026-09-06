// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

public class LinkTargetSpanTests
{
    /// <summary>
    /// Finds the target in a line that is nothing but the reference, and returns the text of it,
    /// which is what the assertions are actually about.
    /// </summary>
    private static string? TargetIn(string reference)
    {
        (int Start, int End)? span = LinkTargetSpan.Find(reference, 0, reference.Length);

        return span is { } found ? reference[found.Start..found.End] : null;
    }

    [Fact]
    public void The_target_of_a_link_is_found()
    {
        TargetIn("[the readme](./nope.md)").ShouldBe("./nope.md");
    }

    [Fact]
    public void The_target_of_an_image_is_found()
    {
        TargetIn("![a diagram](missing.png)").ShouldBe("missing.png");
    }

    [Fact]
    public void A_label_containing_brackets_does_not_confuse_it()
    {
        // Working back from the closing paren rather than forward from the front is what makes
        // this work: the front is the part that varies.
        TargetIn("[see (this) note](gone.md)").ShouldBe("gone.md");
    }

    [Fact]
    public void A_title_after_the_target_is_left_alone()
    {
        // Replacing the whole parenthesis would silently drop the title.
        TargetIn("[a](old.md \"The title\")").ShouldBe("old.md");
    }

    [Fact]
    public void An_empty_target_is_still_a_span_to_write_into()
    {
        TargetIn("[a]()").ShouldBe("");
    }

    [Fact]
    public void An_anchor_is_a_target_like_any_other()
    {
        TargetIn("[jump](#getting-stated)").ShouldBe("#getting-stated");
    }

    [Fact]
    public void The_span_is_located_within_the_whole_line_not_just_the_reference()
    {
        const string line = "intro see [here](./gone.md) please";

        (int Start, int End)? span = LinkTargetSpan.Find(line, 10, 27);

        span.ShouldNotBeNull();
        line[span!.Value.Start..span.Value.End].ShouldBe("./gone.md");
    }

    [Fact]
    public void A_reference_style_link_is_refused_because_there_is_no_target_to_rewrite()
    {
        // "[label][ref]" points at a definition elsewhere. Rewriting in place would be wrong.
        TargetIn("[the readme][docs]").ShouldBeNull();
    }

    [Fact]
    public void A_line_that_has_moved_on_since_the_check_is_refused()
    {
        // The decoration says the reference ends here; the text says otherwise. Editing on that
        // basis would corrupt whatever is actually there now.
        LinkTargetSpan.Find("[a](b.md) and more", 0, 14).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("()")]
    [InlineData("[a]")]
    public void Something_that_is_not_a_reference_is_refused(string text)
    {
        TargetIn(text).ShouldBeNull();
    }

    [Fact]
    public void A_span_reaching_outside_the_line_is_refused_rather_than_throwing()
    {
        LinkTargetSpan.Find("[a](b.md)", 0, 99).ShouldBeNull();
    }
}
