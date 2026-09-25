// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The in-memory list of comments behind a review: reading order, the numbers a reader sees,
/// and whether ending now would lose anything the reviewer has not sent.
/// </summary>
public sealed class ReviewSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 14, 32, 0, TimeSpan.Zero);

    private static ReviewSession NewSession() => new(Guid.NewGuid(), "# Doc\n\nText.\n", Now);

    private static ReviewAnchor At(int line, int start, string quote = "x") => new(line, 0, start, start + quote.Length, quote);

    /// <summary>Numbers run down the page, whatever order the comments were written in.</summary>
    [Fact]
    public void Comments_are_numbered_in_reading_order()
    {
        ReviewSession session = NewSession();

        ReviewComment later = session.Add(At(9, 0), "later");
        ReviewComment earlierOnSameLine = session.Add(At(2, 5), "second on line 2");
        ReviewComment first = session.Add(At(2, 1), "first");

        session.Ordered.Select(c => c.Note).ShouldBe(["first", "second on line 2", "later"]);
        session.NumberOf(first.Id).ShouldBe(1);
        session.NumberOf(earlierOnSameLine.Id).ShouldBe(2);
        session.NumberOf(later.Id).ShouldBe(3);
    }

    /// <summary>A comment added above the others takes their number and pushes them down.</summary>
    [Fact]
    public void A_comment_added_above_renumbers_the_rest()
    {
        ReviewSession session = NewSession();
        ReviewComment below = session.Add(At(5, 0), "below");

        session.Add(At(1, 0), "above");

        session.NumberOf(below.Id).ShouldBe(2);
    }

    [Fact]
    public void An_unknown_comment_has_no_number() =>
        NewSession().NumberOf(Guid.NewGuid()).ShouldBe(0);

    /// <summary>An empty session has nothing to lose.</summary>
    [Fact]
    public void A_new_session_has_nothing_unshared()
    {
        ReviewSession session = NewSession();

        session.HasUnshared.ShouldBeFalse();
        session.IsShared.ShouldBeFalse();
    }

    [Fact]
    public void Adding_a_comment_leaves_it_unshared_until_a_share()
    {
        ReviewSession session = NewSession();

        session.Add(At(2, 0), "note");
        session.HasUnshared.ShouldBeTrue();

        session.MarkShared(Now);

        session.HasUnshared.ShouldBeFalse();
        session.IsShared.ShouldBeTrue();
        session.SharedUtc.ShouldBe(Now);
    }

    /// <summary>Any change after a share - an edit, a new comment, a deletion - is unshared again.</summary>
    [Fact]
    public void A_change_after_a_share_is_unshared()
    {
        ReviewSession session = NewSession();
        ReviewComment one = session.Add(At(2, 0), "one");
        ReviewComment two = session.Add(At(3, 0), "two");
        session.MarkShared(Now);

        session.Update(one.Id, "one, edited").ShouldBeTrue();
        session.HasUnshared.ShouldBeTrue();

        session.MarkShared(Now);
        session.Remove(two.Id).ShouldBeTrue();
        session.HasUnshared.ShouldBeTrue();
    }

    /// <summary>Deleting the last comment leaves nothing to warn about, shared or not.</summary>
    [Fact]
    public void Removing_every_comment_leaves_nothing_unshared()
    {
        ReviewSession session = NewSession();
        ReviewComment only = session.Add(At(2, 0), "only");

        session.Remove(only.Id);

        session.HasUnshared.ShouldBeFalse();
        session.Count.ShouldBe(0);
    }

    /// <summary>Saving a note unchanged is not a change, so it does not undo a share.</summary>
    [Fact]
    public void An_update_that_changes_nothing_is_not_a_change()
    {
        ReviewSession session = NewSession();
        ReviewComment one = session.Add(At(2, 0), "same");
        session.MarkShared(Now);

        session.Update(one.Id, "same").ShouldBeFalse();

        session.IsShared.ShouldBeTrue();
    }

    [Fact]
    public void Updating_or_removing_an_unknown_comment_does_nothing()
    {
        ReviewSession session = NewSession();

        session.Update(Guid.NewGuid(), "x").ShouldBeFalse();
        session.Remove(Guid.NewGuid()).ShouldBeFalse();
        session.Revision.ShouldBe(0);
    }

    [Fact]
    public void An_update_keeps_the_anchor()
    {
        ReviewSession session = NewSession();
        ReviewComment one = session.Add(At(2, 4, "Text"), "before");

        session.Update(one.Id, "after");

        session.Find(one.Id).ShouldBe(one with { Note = "after" });
    }
}
