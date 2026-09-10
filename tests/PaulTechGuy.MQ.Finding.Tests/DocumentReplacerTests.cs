// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Finding.Tests;

public class DocumentReplacerTests
{
    // ------------------------------------------------------------------ literals

    [Fact]
    public void Every_match_on_a_line_is_replaced()
    {
        Text(Run("foo and foo", "foo", "bar")).ShouldBe("bar and bar");
    }

    [Fact]
    public void Matches_across_lines_are_all_replaced()
    {
        Text(Run("the foo\nnothing\nfoo again", "foo", "bar")).ShouldBe("the bar\nnothing\nbar again");
    }

    [Fact]
    public void Overlapping_candidates_are_replaced_the_way_they_were_found()
    {
        // Two matches rather than three, so "aaaa" becomes "bb" and not "bba" or "b".
        Text(Run("aaaa", "aa", "b")).ShouldBe("bb");
    }

    [Fact]
    public void An_empty_replacement_deletes_the_match()
    {
        Text(Run("keep foo this", "foo ", string.Empty)).ShouldBe("keep this");
    }

    [Fact]
    public void An_empty_term_changes_nothing()
    {
        ReplaceResults results = Run("plenty of text here", string.Empty, "x");

        results.TotalMatches.ShouldBe(0);
        results.Documents.ShouldBeEmpty();
        results.Error.ShouldBeNull();
    }

    [Fact]
    public void A_document_with_no_matches_is_left_out_entirely()
    {
        Run("nothing to see", "foo", "bar").Documents.ShouldBeEmpty();
    }

    [Fact]
    public void Case_is_ignored_unless_it_is_asked_about()
    {
        Text(Run("Foo foo", "foo", "bar")).ShouldBe("bar bar");
    }

    [Fact]
    public void Match_case_leaves_the_other_casing_alone()
    {
        Text(Run("Foo foo", "foo", "bar", matchCase: true)).ShouldBe("Foo bar");
    }

    [Fact]
    public void Whole_word_ignores_a_term_buried_in_a_longer_word()
    {
        Text(Run("afoot foo", "foo", "bar", wholeWord: true)).ShouldBe("afoot bar");
    }

    [Fact]
    public void A_dollar_in_a_literal_replacement_is_just_a_dollar()
    {
        // Literal mode has no pattern for it to refer back to, so nothing is substituted.
        Text(Run("cost foo", "foo", "$1")).ShouldBe("cost $1");
    }

    // ------------------------------------------------------------------- groups

    [Fact]
    public void A_numbered_group_is_substituted()
    {
        Text(Run("paul@example", @"(\w+)@(\w+)", "$2@$1", useRegex: true)).ShouldBe("example@paul");
    }

    [Fact]
    public void A_named_group_is_substituted()
    {
        Text(Run("paul@example", @"(?<user>\w+)@(?<host>\w+)", "${host}:${user}", useRegex: true))
            .ShouldBe("example:paul");
    }

    [Fact]
    public void The_whole_match_is_available_as_a_group()
    {
        Text(Run("version 12 and 345", @"\d+", "[$&]", useRegex: true)).ShouldBe("version [12] and [345]");
    }

    [Fact]
    public void Two_dollars_mean_one_dollar()
    {
        Text(Run("costs 12", @"\d+", "$$$&", useRegex: true)).ShouldBe("costs $12");
    }

    [Fact]
    public void Groups_are_substituted_on_every_match_separately()
    {
        Text(Run("a1 b2", @"([a-z])(\d)", "$2$1", useRegex: true)).ShouldBe("1a 2b");
    }

    // ---------------------------------------------------------------- the finder

    [Fact]
    public void A_pattern_matching_nothing_replaces_nothing()
    {
        // a* matches an empty string at every column. The finder skips those, so a replace must
        // not quietly insert itself between every character.
        ReplaceResults results = Run("bbb", "a*", "X", useRegex: true);

        results.TotalMatches.ShouldBe(0);
        results.Documents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("foo and foo\nfoo", "foo", false, false, false)]
    [InlineData("Foo foo FOO", "foo", true, false, false)]
    [InlineData("afoot foo food", "foo", false, true, false)]
    [InlineData("version 12 and 345", @"\d+", false, false, true)]
    [InlineData("a1 b2 c3", @"([a-z])(\d)", false, false, true)]
    [InlineData("bbb", "a*", false, false, true)]
    [InlineData("afoot foo", "fo+", false, true, true)]
    public void The_replacer_finds_exactly_what_the_finder_finds(
        string text,
        string term,
        bool matchCase,
        bool wholeWord,
        bool useRegex)
    {
        FindQuery find = Query(term, matchCase, wholeWord, useRegex);
        FindDocument document = Doc("one.md", text);

        int found = DocumentFinder
            .Find(find, [document], TestContext.Current.CancellationToken)
            .TotalMatches;

        int replaced = DocumentReplacer
            .Replace(
                new ReplaceQuery { Find = find, Replacement = "x" },
                [document],
                TestContext.Current.CancellationToken)
            .TotalMatches;

        replaced.ShouldBe(found);
    }

    // ----------------------------------------------------------------- integrity

    [Fact]
    public void Line_endings_survive_whatever_mix_they_arrived_in()
    {
        Text(Run("foo\r\nfoo\nfoo\rfoo", "foo", "bar")).ShouldBe("bar\r\nbar\nbar\rbar");
    }

    [Fact]
    public void The_text_around_a_match_is_left_exactly_as_it_was()
    {
        Text(Run("  indented foo  \n\ttabbed", "foo", "bar")).ShouldBe("  indented bar  \n\ttabbed");
    }

    [Fact]
    public void The_document_it_was_scanned_from_is_carried_back()
    {
        ReplaceDocumentResult replaced = Run("foo", "foo", "bar").Documents[0];

        replaced.OriginalText.ShouldBe("foo");
        replaced.NewText.ShouldBe("bar");
        replaced.Count.ShouldBe(1);
    }

    [Fact]
    public void Documents_are_reported_in_the_order_they_were_given()
    {
        ReplaceResults results = Replace(
            Query("foo"),
            "bar",
            Doc("a.md", "foo"),
            Doc("b.md", "nothing here"),
            Doc("c.md", "foo foo"));

        results.Documents.Select(document => document.Name).ShouldBe(["a.md", "c.md"]);
        results.TotalMatches.ShouldBe(3);
    }

    // ------------------------------------------------------------------ refusals

    [Fact]
    public void An_invalid_pattern_is_reported_rather_than_thrown()
    {
        ReplaceResults results = Run("anything", "(unclosed", "x", useRegex: true);

        results.Error.ShouldNotBeNullOrWhiteSpace();
        results.Documents.ShouldBeEmpty();
        results.TotalMatches.ShouldBe(0);
    }

    [Theory]
    [InlineData("${unclosed", "${unclosed")]
    [InlineData("${undefined}", "${undefined}")]
    [InlineData("$99", "$99")]
    [InlineData("$-", "$-")]
    public void A_replacement_the_engine_does_not_recognise_is_taken_literally(
        string replacement,
        string expected)
    {
        // There is no such thing as a replacement the user got wrong: .NET's parser treats every
        // sequence it does not recognise as literal text rather than rejecting it. Worth pinning
        // down, because it is the reason Replace has no error path for the replacement box.
        Text(Run("foo", "foo", replacement, useRegex: true)).ShouldBe(expected);
    }

    [Fact]
    public void A_runaway_pattern_gives_up_rather_than_hanging()
    {
        // Catastrophic backtracking, abandoned by the budget DocumentFinder sets.
        ReplaceResults results = Run(
            new string('a', 30) + "!",
            "(a+)+$",
            "x",
            useRegex: true);

        results.Error.ShouldNotBeNullOrWhiteSpace();
        results.Documents.ShouldBeEmpty();
    }

    [Fact]
    public void The_ceiling_stops_the_scan_and_says_so()
    {
        ReplaceResults results = Run(Lines(DocumentFinder.MatchLimit + 50), "x", "y");

        results.Truncated.ShouldBeTrue();
        results.TotalMatches.ShouldBe(DocumentFinder.MatchLimit);
    }

    [Fact]
    public void Exactly_the_ceiling_is_not_truncated()
    {
        ReplaceResults results = Run(Lines(DocumentFinder.MatchLimit), "x", "y");

        results.Truncated.ShouldBeFalse();
        results.TotalMatches.ShouldBe(DocumentFinder.MatchLimit);
    }

    [Fact]
    public void A_cancelled_replace_stops_rather_than_finishing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Should.Throw<OperationCanceledException>(
            () => DocumentReplacer.Replace(
                new ReplaceQuery { Find = Query("x"), Replacement = "y" },
                [Doc("a.md", Lines(1000))],
                cancellation.Token));
    }

    // ------------------------------------------------------------------- helpers

    /// <summary>A replace over one document, which is what most of these want.</summary>
    private static ReplaceResults Run(
        string text,
        string term,
        string replacement,
        bool matchCase = false,
        bool wholeWord = false,
        bool useRegex = false) =>
        Replace(Query(term, matchCase, wholeWord, useRegex), replacement, Doc("one.md", text));

    private static ReplaceResults Replace(
        FindQuery find,
        string replacement,
        params FindDocument[] documents) =>
        DocumentReplacer.Replace(
            new ReplaceQuery { Find = find, Replacement = replacement },
            documents,
            TestContext.Current.CancellationToken);

    private static FindQuery Query(
        string term,
        bool matchCase = false,
        bool wholeWord = false,
        bool useRegex = false) =>
        new()
        {
            Term = term,
            MatchCase = matchCase,
            WholeWord = wholeWord,
            UseRegex = useRegex,
        };

    private static FindDocument Doc(string name, string text) =>
        new(Guid.NewGuid(), name, $@"C:\docs\{name}", text);

    /// <summary>A document of <paramref name="count"/> lines, each holding one "x".</summary>
    private static string Lines(int count) => string.Concat(Enumerable.Repeat("x\n", count));

    /// <summary>The new text of the one document that changed.</summary>
    private static string Text(ReplaceResults results) => results.Documents[0].NewText;
}
