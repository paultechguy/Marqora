// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Finding.Tests;

public class DocumentFinderTests
{
    // ------------------------------------------------------------------ literals

    [Fact]
    public void A_term_is_found_on_every_line_that_holds_it()
    {
        Hits(Run("the foo\nnothing\nfoo again", "foo")).ShouldBe([(0, 4, 3), (2, 0, 3)]);
    }

    [Fact]
    public void Several_matches_on_one_line_are_all_reported()
    {
        Hits(Run("foo and foo", "foo")).ShouldBe([(0, 0, 3), (0, 8, 3)]);
    }

    [Fact]
    public void Overlapping_candidates_are_not_reported_twice()
    {
        // Two matches rather than three, which is what the editor's own find does.
        Hits(Run("aaaa", "aa")).ShouldBe([(0, 0, 2), (0, 2, 2)]);
    }

    [Fact]
    public void Case_is_ignored_unless_it_is_asked_about()
    {
        Hits(Run("Foo foo", "foo")).ShouldBe([(0, 0, 3), (0, 4, 3)]);
    }

    [Fact]
    public void Match_case_leaves_the_other_casing_alone()
    {
        Hits(Run("Foo foo", "foo", matchCase: true)).ShouldBe([(0, 4, 3)]);
    }

    [Fact]
    public void An_empty_term_finds_nothing()
    {
        FindResults results = Run("plenty of text here", string.Empty);

        results.TotalMatches.ShouldBe(0);
        results.Error.ShouldBeNull();
    }

    [Fact]
    public void A_term_of_spaces_is_searched_for_like_any_other()
    {
        // Two spaces at the end of a line are a markdown line break, so this is a real thing
        // to go looking for.
        Hits(Run("line  \nnext", "  ")).ShouldBe([(0, 4, 2)]);
    }

    [Fact]
    public void An_empty_document_is_searched_without_complaint()
    {
        Run(string.Empty, "foo").TotalMatches.ShouldBe(0);
    }

    // ---------------------------------------------------------------- whole word

    [Fact]
    public void Whole_word_ignores_a_term_buried_in_a_longer_word()
    {
        Hits(Run("afoot foo", "foo", wholeWord: true)).ShouldBe([(0, 6, 3)]);
    }

    [Fact]
    public void Whole_word_counts_an_underscore_as_part_of_the_word()
    {
        Run("foo_bar", "foo", wholeWord: true).TotalMatches.ShouldBe(0);
    }

    [Fact]
    public void Whole_word_counts_a_digit_as_part_of_the_word()
    {
        Run("foo2", "foo", wholeWord: true).TotalMatches.ShouldBe(0);
    }

    [Fact]
    public void Whole_word_accepts_punctuation_on_either_side()
    {
        Hits(Run("(foo)", "foo", wholeWord: true)).ShouldBe([(0, 1, 3)]);
    }

    [Fact]
    public void Whole_word_accepts_a_match_that_fills_the_line()
    {
        Hits(Run("foo", "foo", wholeWord: true)).ShouldBe([(0, 0, 3)]);
    }

    // --------------------------------------------------------------- line breaks

    [Fact]
    public void Crlf_endings_count_as_one_break()
    {
        Hits(Run("alpha\r\nfoo bravo\r\ncharlie", "foo")).ShouldBe([(1, 0, 3)]);
    }

    [Fact]
    public void A_bare_carriage_return_ends_a_line_too()
    {
        Hits(Run("alpha\rfoo", "foo")).ShouldBe([(1, 0, 3)]);
    }

    [Fact]
    public void Mixed_endings_still_agree_with_the_editor()
    {
        Hits(Run("a\r\nb\nc\rfoo", "foo")).ShouldBe([(3, 0, 3)]);
    }

    [Fact]
    public void A_match_on_a_last_line_with_no_break_after_it_is_found()
    {
        Hits(Run("alpha\nfoo", "foo")).ShouldBe([(1, 0, 3)]);
    }

    [Fact]
    public void A_trailing_break_does_not_invent_a_match()
    {
        Hits(Run("foo\n", "foo")).ShouldBe([(0, 0, 3)]);
    }

    // ------------------------------------------------------------------ the line

    [Fact]
    public void A_match_carries_the_whole_line_it_was_found_on()
    {
        FindMatch match = Run("one\nthe foo line\n", "foo").Documents[0].Matches[0];

        match.LineText.ShouldBe("the foo line");
        match.Text.ShouldBe("foo");
    }

    [Fact]
    public void Matches_on_one_line_share_a_single_line_string()
    {
        // The results list holds every match until the next search. Handing each one its own
        // copy of a long line is how a search of a large document quietly costs megabytes.
        IReadOnlyList<FindMatch> matches = Run("foo and foo", "foo").Documents[0].Matches;

        matches[1].LineText.ShouldBeSameAs(matches[0].LineText);
    }

    // ------------------------------------------------------- regular expressions

    [Fact]
    public void A_regular_expression_reports_the_span_it_matched()
    {
        Hits(Run("version 12 and 345", @"\d+", useRegex: true)).ShouldBe([(0, 8, 2), (0, 15, 3)]);
    }

    [Fact]
    public void A_regular_expression_anchor_binds_to_its_own_line()
    {
        // Each line is matched as an input of its own, which is what makes ^ mean "start of
        // this line" without Multiline entering into it.
        Hits(Run("foo bar\nbar foo", "^foo", useRegex: true)).ShouldBe([(0, 0, 3)]);
    }

    [Fact]
    public void A_regular_expression_ignores_case_unless_it_is_asked_about()
    {
        Hits(Run("Foo", "foo", useRegex: true)).ShouldBe([(0, 0, 3)]);
        Run("Foo", "foo", matchCase: true, useRegex: true).TotalMatches.ShouldBe(0);
    }

    [Fact]
    public void A_pattern_that_can_match_nothing_reports_nothing()
    {
        // a* matches an empty string at every column. Those are rows pointing at no text.
        Run("bbb", "a*", useRegex: true).TotalMatches.ShouldBe(0);
    }

    [Fact]
    public void Whole_word_applies_to_a_regular_expression_too()
    {
        Hits(Run("afoot foo", "fo+", wholeWord: true, useRegex: true)).ShouldBe([(0, 6, 3)]);
    }

    [Fact]
    public void An_invalid_pattern_is_reported_rather_than_thrown()
    {
        FindResults results = Run("anything", "(unclosed", useRegex: true);

        results.Error.ShouldNotBeNullOrWhiteSpace();
        results.Documents.ShouldBeEmpty();
        results.TotalMatches.ShouldBe(0);
    }

    [Fact]
    public void An_invalid_pattern_is_only_a_problem_in_regular_expression_mode()
    {
        Hits(Run("an (unclosed thing", "(unclosed")).ShouldBe([(0, 3, 9)]);
    }

    // ----------------------------------------------------------- many documents

    [Fact]
    public void Documents_are_reported_in_the_order_they_were_given()
    {
        FindResults results = Search(
            Query("foo"),
            Doc("a.md", "foo"),
            Doc("b.md", "nothing here"),
            Doc("c.md", "foo foo"));

        results.Documents.Select(document => document.Name).ShouldBe(["a.md", "c.md"]);
        results.TotalMatches.ShouldBe(3);
    }

    [Fact]
    public void A_document_carries_its_identity_through_to_its_matches()
    {
        FindDocument document = Doc("notes.md", "foo");

        FindDocumentMatches matched = Search(Query("foo"), document).Documents[0];

        matched.DocumentId.ShouldBe(document.Id);
        matched.Path.ShouldBe(document.Path);
    }

    // ------------------------------------------------------------- the ceiling

    [Fact]
    public void The_search_stops_at_its_ceiling_and_says_so()
    {
        FindResults results = Run(Lines(DocumentFinder.MatchLimit + 50), "x");

        results.TotalMatches.ShouldBe(DocumentFinder.MatchLimit);
        results.Truncated.ShouldBeTrue();
        results.Documents.Sum(document => document.Matches.Count).ShouldBe(DocumentFinder.MatchLimit);
    }

    [Fact]
    public void Exactly_the_ceiling_is_not_a_truncated_search()
    {
        FindResults results = Run(Lines(DocumentFinder.MatchLimit), "x");

        results.TotalMatches.ShouldBe(DocumentFinder.MatchLimit);
        results.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void The_ceiling_counts_across_documents_rather_than_within_one()
    {
        FindResults results = Search(
            Query("x"),
            Doc("a.md", Lines(DocumentFinder.MatchLimit - 1)),
            Doc("b.md", Lines(5)));

        results.TotalMatches.ShouldBe(DocumentFinder.MatchLimit);
        results.Truncated.ShouldBeTrue();
        results.Documents[1].Matches.Count.ShouldBe(1);
    }

    // ----------------------------------------------------------------- LineAt

    [Fact]
    public void A_line_can_be_read_back_by_the_number_a_match_reported()
    {
        FindMatch match = Run("alpha\r\nthe foo line\nbravo", "foo").Documents[0].Matches[0];

        DocumentFinder.LineAt("alpha\r\nthe foo line\nbravo", match.Line).ShouldBe("the foo line");
    }

    [Fact]
    public void LineAt_counts_breaks_the_way_the_finder_does()
    {
        DocumentFinder.LineAt("a\r\nb\nc\rd", 3).ShouldBe("d");
    }

    [Fact]
    public void LineAt_sees_the_empty_line_after_a_trailing_break()
    {
        DocumentFinder.LineAt("a\n", 1).ShouldBe(string.Empty);
    }

    [Fact]
    public void LineAt_returns_null_past_the_end()
    {
        DocumentFinder.LineAt("a\nb", 2).ShouldBeNull();
        DocumentFinder.LineAt("a\nb", -1).ShouldBeNull();
    }

    // --------------------------------------------------------------- cancelling

    [Fact]
    public void A_cancelled_search_stops_rather_than_finishing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Should.Throw<OperationCanceledException>(
            () => DocumentFinder.Find(Query("x"), [Doc("a.md", Lines(1000))], cancellation.Token));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A search of one document, which is what most of these want.</summary>
    private static FindResults Run(
        string text,
        string term,
        bool matchCase = false,
        bool wholeWord = false,
        bool useRegex = false) =>
        Search(Query(term, matchCase, wholeWord, useRegex), Doc("one.md", text));

    /// <summary>
    /// A search of however many documents. The token comes from the test context, so a run
    /// stopped part-way does not sit waiting on a finder nobody is listening to.
    /// </summary>
    private static FindResults Search(FindQuery query, params FindDocument[] documents) =>
        DocumentFinder.Find(query, documents, TestContext.Current.CancellationToken);

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

    /// <summary>Every match in the results, flattened, as line, column and length.</summary>
    private static (int Line, int Column, int Length)[] Hits(FindResults results) =>
        [.. results.Documents
            .SelectMany(document => document.Matches)
            .Select(match => (match.Line, match.Column, match.Length))];
}
