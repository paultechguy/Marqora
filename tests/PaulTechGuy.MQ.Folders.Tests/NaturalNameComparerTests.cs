// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folders.Tests;

public sealed class NaturalNameComparerTests
{
    private static List<string> Sorted(params string[] names) =>
        [.. names.Order(NaturalNameComparer.Instance)];

    [Fact]
    public void Digit_runs_compare_as_numbers() =>
        Sorted("Chapter 10", "Chapter 2", "Chapter 1").ShouldBe(["Chapter 1", "Chapter 2", "Chapter 10"]);

    [Fact]
    public void Letters_ignore_case() =>
        Sorted("beta.md", "Alpha.md", "gamma.md").ShouldBe(["Alpha.md", "beta.md", "gamma.md"]);

    [Fact]
    public void A_prefix_comes_first() =>
        Sorted("notes-old.md", "notes", "notes.md").First().ShouldBe("notes");

    [Fact]
    public void Numbers_longer_than_a_long_do_not_overflow() =>
        Sorted("v99999999999999999999999", "v100000000000000000000000", "v2")
            .ShouldBe(["v2", "v99999999999999999999999", "v100000000000000000000000"]);

    [Fact]
    public void Ties_are_broken_so_the_order_is_total()
    {
        NaturalNameComparer.Instance.Compare("a.md", "A.md").ShouldNotBe(0);
        NaturalNameComparer.Instance.Compare("file1", "file01").ShouldNotBe(0);

        // And the same whichever comes first.
        Sorted("a.md", "A.md").ShouldBe(Sorted("A.md", "a.md"));
    }

    [Fact]
    public void Equal_names_compare_equal() =>
        NaturalNameComparer.Instance.Compare("same 2.md", "same 2.md").ShouldBe(0);
}
