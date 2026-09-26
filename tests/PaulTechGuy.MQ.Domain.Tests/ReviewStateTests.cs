// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// The block a shared review page carries so the review can be resumed: that it comes back
/// exactly, that the real block wins over a copy the document itself could hold, and that a
/// page which does not hold together is refused whole.
/// </summary>
public sealed class ReviewStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 15, 0, TimeSpan.FromHours(-7));

    private const string Source = "# Plan\n\nThe server is the source of truth.\n\n| a | b |\n|---|---|\n| one | two |\n";

    private static ReviewSession SessionWith(string source = Source)
    {
        var session = new ReviewSession(Guid.NewGuid(), source, Now);

        session.Add(new ReviewAnchor(2, 0, 4, 10, "server"), "Which one?");
        session.Add(new ReviewAnchor(0, 0, 0, 4, "Plan"), "Rename.");

        return session;
    }

    private static ReviewState StateOf(ReviewSession session) =>
        ReviewState.For(session, "notes.md", Guid.NewGuid(), 1, Now, "1.0.11");

    /// <summary>A page shaped as the writer shapes it: the article, the CriticMarkup block, then the state last.</summary>
    private static string Page(ReviewState state, string article = "<p>Body</p>", string? source = null) =>
        "<!DOCTYPE html><html><head>" + ReviewState.MarkerMeta + "<title>notes — Review</title></head>"
        + "<body class=\"mq-review\"><article class=\"mq-preview\">" + article + "</article>\n"
        + ReviewPage.SourceBlock(source ?? state.Source) + "\n"
        + ReviewState.Encode(state) + "\n</body></html>";

    /// <summary>Replaces the encoded block with one whose JSON has been changed by hand.</summary>
    private static string WithJson(ReviewState state, Func<string, string> edit)
    {
        string encoded = ReviewState.Encode(state);
        int start = encoded.IndexOf('>') + 1;
        int end = encoded.LastIndexOf("</script>", StringComparison.Ordinal);
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded[start..end]));
        string changed = Convert.ToBase64String(Encoding.UTF8.GetBytes(edit(json)));

        return Page(state).Replace(encoded[start..end], changed, StringComparison.Ordinal);
    }

    // ---- round trip

    [Fact]
    public void The_state_comes_back_with_its_comments_in_reading_order()
    {
        ReviewSession session = SessionWith();
        ReviewState state = StateOf(session);

        ReviewState? read = ReviewState.TryDecode(Page(state), out ReviewStateProblem problem);

        problem.ShouldBe(ReviewStateProblem.None);
        read.ShouldNotBeNull();
        read.SessionId.ShouldBe(session.SessionId);
        read.WriteId.ShouldBe(state.WriteId);
        read.FileName.ShouldBe("notes.md");
        read.Source.ShouldBe(Source);
        read.Comments.Select(c => c.Note).ShouldBe(["Rename.", "Which one?"]);
        read.Comments[1].ShouldBe(new ReviewStateComment { Id = read.Comments[1].Id, Line = 2, Index = 0, Start = 4, End = 10, Quote = "server", Note = "Which one?" });
    }

    /// <summary>The CriticMarkup copy is lossy about these; the state must not be.</summary>
    [Fact]
    public void Text_the_source_block_would_escape_comes_back_exactly()
    {
        string source = (char)0xFEFF + "Use </script> and <!-- and <script> and <\\script.\r\nEmoji 🎉 and CRLF.\r\n";
        var session = new ReviewSession(Guid.NewGuid(), source, Now);
        session.Add(new ReviewAnchor(0, 0, 1, 4, "Use"), "Note </script> too.");

        ReviewState? read = ReviewState.TryDecode(Page(StateOf(session)));

        read.ShouldNotBeNull();
        read.Source.ShouldBe(source);
        read.Comments[0].Note.ShouldBe("Note </script> too.");
    }

    /// <summary>Times are written in UTC, so a page does not carry where its reviewer is.</summary>
    [Fact]
    public void Times_are_written_in_utc()
    {
        ReviewState state = StateOf(SessionWith());

        state.SharedUtc.Offset.ShouldBe(TimeSpan.Zero);
        state.StartedUtc.Offset.ShouldBe(TimeSpan.Zero);
        state.SharedUtc.ShouldBe(Now);
    }

    [Fact]
    public void Restoring_keeps_ids_order_and_numbers_and_counts_as_shared()
    {
        ReviewSession original = SessionWith();
        ReviewState state = ReviewState.TryDecode(Page(StateOf(original)))!;

        ReviewSession restored = ReviewSession.Restore(Guid.NewGuid(), state);

        restored.SessionId.ShouldBe(original.SessionId);
        restored.SourceText.ShouldBe(original.SourceText);
        restored.Ordered.Select(c => c.Id).ShouldBe(original.Ordered.Select(c => c.Id));
        restored.Ordered.Select(c => c.Anchor).ShouldBe(original.Ordered.Select(c => c.Anchor));
        restored.NumberOf(original.Ordered[0].Id).ShouldBe(1);
        restored.HasUnshared.ShouldBeFalse();
        restored.IsShared.ShouldBeTrue();

        restored.Update(restored.Ordered[0].Id, "Changed.");

        restored.HasUnshared.ShouldBeTrue();
    }

    // ---- the real block wins

    /// <summary>
    /// A document can hold raw HTML, so its own text could carry a block with the same id. It
    /// lands in the article, before the real one - and must not be what is read.
    /// </summary>
    [Fact]
    public void A_block_planted_in_the_article_loses_to_the_real_one()
    {
        ReviewState real = StateOf(SessionWith());
        ReviewState forged = StateOf(SessionWith("Forged text.\n")) with { SessionId = Guid.NewGuid() };

        ReviewState? read = ReviewState.TryDecode(Page(real, article: ReviewState.Encode(forged)));

        read.ShouldNotBeNull();
        read.SessionId.ShouldBe(real.SessionId);
        read.Source.ShouldBe(Source);
    }

    // ---- refusals

    [Fact]
    public void A_page_without_the_block_is_missing()
    {
        ReviewState.TryDecode("<html><body><p>Hello</p></body></html>", out ReviewStateProblem problem).ShouldBeNull();
        problem.ShouldBe(ReviewStateProblem.Missing);
    }

    [Fact]
    public void A_damaged_block_is_refused()
    {
        string page = Page(StateOf(SessionWith()));
        int cut = page.LastIndexOf("</script>", StringComparison.Ordinal);
        string damaged = page[..(cut - 40)] + "!!!" + page[cut..];

        ReviewState.TryDecode(damaged, out ReviewStateProblem problem).ShouldBeNull();
        problem.ShouldBe(ReviewStateProblem.Damaged);
    }

    [Fact]
    public void Another_format_is_refused()
    {
        ReviewState.TryDecode(WithJson(StateOf(SessionWith()), j => j.Replace("\"marqora-review\"", "\"marqora-folio\"", StringComparison.Ordinal)), out ReviewStateProblem problem)
            .ShouldBeNull();
        problem.ShouldBe(ReviewStateProblem.Damaged);
    }

    /// <summary>The version is not a gate: a later page is read for what this build understands.</summary>
    [Fact]
    public void A_newer_schema_with_unknown_fields_is_read()
    {
        string page = WithJson(StateOf(SessionWith()), j => j
            .Replace("\"schemaVersion\":1", "\"schemaVersion\":3", StringComparison.Ordinal)
            .Replace("{\"format\"", "{\"reviewerColor\":\"teal\",\"format\"", StringComparison.Ordinal));

        ReviewState? read = ReviewState.TryDecode(page);

        read.ShouldNotBeNull();
        read.IsNewerSchema.ShouldBeTrue();
        read.Comments.Count.ShouldBe(2);
    }

    /// <summary>What a later format sets on purpose when an earlier build must not read it.</summary>
    [Fact]
    public void A_page_that_asks_for_a_later_reader_is_refused()
    {
        ReviewState.TryDecode(WithJson(StateOf(SessionWith()), j => j.Replace("\"minimumReader\":1", "\"minimumReader\":2", StringComparison.Ordinal)), out ReviewStateProblem problem)
            .ShouldBeNull();
        problem.ShouldBe(ReviewStateProblem.TooNew);
    }

    [Fact]
    public void Text_that_does_not_match_its_hash_is_refused()
    {
        ReviewState state = StateOf(SessionWith()) with { Source = Source.Replace("truth", "fiction", StringComparison.Ordinal) };

        ReviewState.TryDecode(Page(state), out ReviewStateProblem problem).ShouldBeNull();
        problem.ShouldBe(ReviewStateProblem.Inconsistent);
    }

    public static TheoryData<ReviewStateComment> BadAnchors => new()
    {
        new ReviewStateComment { Id = Guid.NewGuid(), Line = 99, Start = 0, End = 3, Quote = "abc" },
        new ReviewStateComment { Id = Guid.NewGuid(), Line = -1, Start = 0, End = 3, Quote = "abc" },
        new ReviewStateComment { Id = Guid.NewGuid(), Line = 0, Index = -1, Start = 0, End = 3, Quote = "abc" },
        new ReviewStateComment { Id = Guid.NewGuid(), Line = 0, Start = 3, End = 3, Quote = "abc" },
        new ReviewStateComment { Id = Guid.NewGuid(), Line = 0, Start = 0, End = 3, Quote = string.Empty },
        new ReviewStateComment { Id = Guid.Empty, Line = 0, Start = 0, End = 3, Quote = "abc" },
        new ReviewStateComment { Id = Guid.NewGuid(), Line = 0, Start = 0, End = 3, Quote = "abc", Note = new string('x', ReviewState.MaximumTextLength + 1) },
    };

    [Theory]
    [MemberData(nameof(BadAnchors))]
    public void An_anchor_that_points_nowhere_is_refused(ReviewStateComment bad)
    {
        ReviewState state = StateOf(SessionWith()) with { Comments = [bad] };

        ReviewState.TryDecode(Page(state), out ReviewStateProblem problem).ShouldBeNull();
        problem.ShouldBe(ReviewStateProblem.Inconsistent);
    }

    [Fact]
    public void Two_comments_with_one_id_are_refused()
    {
        ReviewState state = StateOf(SessionWith());
        ReviewState twice = state with { Comments = [state.Comments[0], state.Comments[0]] };

        ReviewState.TryDecode(Page(twice)).ShouldBeNull();
    }

    [Fact]
    public void Too_many_comments_are_refused()
    {
        ReviewState state = StateOf(SessionWith());
        ReviewStateComment[] many = [.. Enumerable.Range(0, ReviewState.MaximumComments + 1)
            .Select(_ => state.Comments[0] with { Id = Guid.NewGuid() })];

        ReviewState.TryDecode(Page(state with { Comments = many })).ShouldBeNull();
    }

    // ---- recognizing a page

    [Fact]
    public void A_review_page_is_recognized_from_its_head()
    {
        string head = Page(StateOf(SessionWith()))[..200];

        ReviewState.IsReviewPage(head).ShouldBeTrue();
        ReviewState.IsLegacyReviewPage(head).ShouldBeFalse();
    }

    /// <summary>The head a 1.0.10 page has: no marker, Marqora's generator, and the review title.</summary>
    [Fact]
    public void A_page_from_before_resuming_is_recognized_as_legacy()
    {
        const string head = """
            <!DOCTYPE html>
            <html lang="en" data-theme="light">
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>notes — Review</title>
            <meta name="generator" content="Marqora" />
            <style>
            """;

        ReviewState.IsReviewPage(head).ShouldBeFalse();
        ReviewState.IsLegacyReviewPage(head).ShouldBeTrue();
        ReviewState.IsLegacyReviewPage(head.Replace(" — Review", string.Empty, StringComparison.Ordinal)).ShouldBeFalse();
    }

    // ---- file names

    [Fact]
    public void A_file_name_is_cleaned_before_it_is_shown()
    {
        string rlo = ((char)0x202E).ToString();

        ReviewState.SanitizeFileName("notes.md").ShouldBe("notes.md");
        ReviewState.SanitizeFileName(@"C:\Users\someone\notes.md").ShouldBe("notes.md");
        ReviewState.SanitizeFileName("../../notes.md").ShouldBe("notes.md");
        ReviewState.SanitizeFileName("notes" + rlo + "dm.exe").ShouldBe("notesdm.exe");
        ReviewState.SanitizeFileName("no\ttes.md").ShouldBe("notes.md");
        ReviewState.SanitizeFileName("   ").ShouldBe("Untitled.md");
        ReviewState.SanitizeFileName(null).ShouldBe("Untitled.md");
        ReviewState.SanitizeFileName(new string('a', 300) + ".md").ShouldBe(new string('a', 117) + ".md");
    }

    [Fact]
    public void A_resumed_label_is_numbered_when_it_is_taken()
    {
        HashSet<string> open = ["notes.md (review)"];

        ReviewPage.ResumeLabel("notes.md", _ => false).ShouldBe("notes.md (review)");
        ReviewPage.ResumeLabel("notes.md", open.Contains).ShouldBe("notes.md (review 2)");
    }
}
