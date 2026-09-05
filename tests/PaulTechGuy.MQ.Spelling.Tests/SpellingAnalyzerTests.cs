// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Spelling.Tests;

/// <summary>
/// The analyzer's job is mostly refusal: nearly every word it is shown is one it must not report.
///
/// Two failures matter most. Reporting a word inside code, a URL or an identifier is what turns
/// the feature into a squiggle storm and gets it switched off - so most of what follows is a
/// place a squiggle must never appear. The other is the cache going stale or lying: it holds raw
/// engine output so that adding a word costs no engine calls, and the test that pins that claim
/// is the one that would catch the design quietly breaking.
/// </summary>
public sealed class SpellingAnalyzerTests
{
    // ------------------------------------------------------------------ the basic report

    [Fact]
    public void A_misspelling_in_prose_is_reported_with_its_position()
    {
        SpellingIssue issue = Check("Please recieve this.", "recieve").ShouldHaveSingleItem();

        issue.Line.ShouldBe(0);
        issue.Start.ShouldBe(7);
        issue.Length.ShouldBe(7);
        issue.Word.ShouldBe("recieve");
        issue.Kind.ShouldBe(SpellingIssueKind.Misspelling);
    }

    [Fact]
    public void Line_numbers_count_from_zero_and_survive_windows_line_endings()
    {
        SpellingIssue issue = Check("First line.\r\nThen recieve it.", "recieve").ShouldHaveSingleItem();

        issue.Line.ShouldBe(1);

        // The stray \r is trimmed from the end, so a column earlier in the line is unaffected.
        issue.Start.ShouldBe(5);
    }

    [Fact]
    public void A_word_repeated_is_reported_as_a_repeat_rather_than_a_misspelling()
    {
        SpellingIssue issue = Check("It has has two.").ShouldHaveSingleItem();

        issue.Kind.ShouldBe(SpellingIssueKind.RepeatedWord);
        issue.Word.ShouldBe("has");
    }

    [Fact]
    public void A_word_either_side_of_a_code_span_is_not_a_word_typed_twice()
    {
        // Marqora's own README, which reported the second "and" as a repeat. The masker used to
        // fill a code span with spaces, which closed the gap and made the two words neighbours;
        // it fills with a separator now. The engine never sees the code either way.
        Check("break, and `^` and `$` anchor to the line.").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("A link to [the guide](./a.md) to the point.")]
    [InlineData("Visit https://example.com/one to the end.")]
    [InlineData("The sum $x + y$ the total.")]
    public void No_mask_makes_the_words_either_side_of_it_adjacent(string line)
    {
        // The same fault as the code span above, in every other thing the masker blanks.
        Check(line).ShouldBeEmpty();
    }

    // ------------------------------------------------------------------- possessives

    [Fact]
    public void A_word_Marqora_ships_knowing_is_still_known_when_made_possessive()
    {
        // The engine flags the whole token, so a plain lookup of "Marqora's" misses the
        // "Marqora" in the seed list. Every word anyone added carried the same hole.
        Check("Marqora's design is deliberate.", "Marqora's").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Zettlr's", "Zettlr")]
    [InlineData("Zettlr’s", "Zettlr")]
    [InlineData("Zettlr'", "Zettlr")]
    public void A_word_the_user_accepted_is_still_known_when_made_possessive(string flagged, string accepted)
    {
        // Both apostrophes, and the bare one that a plural or a classical possessive uses.
        var engine = new FakeSpellingEngine(flagged);
        var analyzer = new SpellingAnalyzer(engine, new FakeUserDictionary(accepted));

        analyzer.Check($"The {flagged} approach.").ShouldBeEmpty();
    }

    [Fact]
    public void A_possessive_of_a_word_nobody_knows_is_still_reported()
    {
        // The retry must not become a way for anything ending in an apostrophe to escape.
        Check("The recieve's problem.", "recieve's").ShouldHaveSingleItem();
    }

    // ------------------------------------------------------- where a squiggle must never appear

    [Fact]
    public void Nothing_inside_a_fenced_block_is_reported()
    {
        Check("```\nrecieve\n```", "recieve").ShouldBeEmpty();
    }

    [Fact]
    public void Nothing_inside_front_matter_is_reported()
    {
        Check("---\ntitle: recieve\n---\n", "recieve").ShouldBeEmpty();
    }

    [Fact]
    public void Nothing_inside_an_indented_code_block_is_reported()
    {
        // The style checks ignore indented blocks to stay in step with the formatter. Spelling
        // cannot afford to: a four-space code sample would be underlined end to end.
        Check("Text.\n\n    recieve();\n", "recieve").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Call `recieve()` now.")]
    [InlineData("See <https://example.com/recieve> for more.")]
    [InlineData("Go to https://example.com/recieve now.")]
    [InlineData("See [the guide](./recieve.md) for more.")]
    [InlineData("A <span class=\"recieve\">word</span> here.")]
    [InlineData("The formula $recieve$ is odd.")]
    public void Nothing_the_masker_blanks_is_reported(string line)
    {
        Check(line, "recieve").ShouldBeEmpty();
    }

    [Fact]
    public void Link_text_is_still_checked_even_though_the_target_is_not()
    {
        // The reader sees the link text, so a misspelling in it is a real one.
        Check("See [recieve the guide](./ok.md).", "recieve").ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("The SDKX is fine.", "SDKX")]
    [InlineData("Built for winx64 here.", "winx64")]
    [InlineData("Call setModelMarkers now.", "setModelMarkers")]
    public void Words_the_skip_rules_account_for_are_not_reported(string line, string wrong)
    {
        Check(line, wrong).ShouldBeEmpty();
    }

    [Fact]
    public void A_word_Marqora_ships_knowing_is_not_reported()
    {
        // "Marqora" has no interior capital, so no skip rule exempts it. Only the seed list does,
        // and without it the welcome document would open covered in underlines.
        Check("Welcome to Marqora today.", "Marqora").ShouldBeEmpty();
    }

    [Fact]
    public void A_word_the_user_has_accepted_is_not_reported()
    {
        var engine = new FakeSpellingEngine("Zettlr");
        var analyzer = new SpellingAnalyzer(engine, new FakeUserDictionary("Zettlr"));

        analyzer.Check("Compared with Zettlr here.").ShouldBeEmpty();
    }

    // --------------------------------------------------------------------------- switched off

    [Fact]
    public void An_unavailable_engine_reports_nothing_and_is_never_called()
    {
        var engine = new FakeSpellingEngine("recieve") { IsAvailable = false };
        var analyzer = new SpellingAnalyzer(engine, new FakeUserDictionary());

        analyzer.Check("Please recieve this.").ShouldBeEmpty();
        engine.CheckCalls.ShouldBe(0);
    }

    [Fact]
    public void An_empty_document_reports_nothing()
    {
        Check("", "recieve").ShouldBeEmpty();
    }

    // ---------------------------------------------------------------------------- the cache

    [Fact]
    public void A_line_already_seen_is_not_sent_to_the_engine_again()
    {
        var engine = new FakeSpellingEngine("recieve");
        var analyzer = new SpellingAnalyzer(engine, new FakeUserDictionary());

        analyzer.Check("Please recieve this.");
        int afterFirst = engine.CheckCalls;

        analyzer.Check("Please recieve this.");

        engine.CheckCalls.ShouldBe(afterFirst);
    }

    [Fact]
    public void A_line_that_moves_down_the_document_is_still_a_cache_hit()
    {
        // Keyed on the line's own text rather than its index, so inserting above it costs nothing.
        var engine = new FakeSpellingEngine("recieve");
        var analyzer = new SpellingAnalyzer(engine, new FakeUserDictionary());

        analyzer.Check("Please recieve this.");
        int afterFirst = engine.CheckCalls;

        analyzer.Check("A new first line.\nPlease recieve this.");

        // One call for the line that is genuinely new, and none for the one that only moved.
        engine.CheckCalls.ShouldBe(afterFirst + 1);
    }

    [Fact]
    public async Task Accepting_a_word_changes_the_answer_without_asking_the_engine_anything()
    {
        // This is the claim the whole cache design rests on, and why Add to Dictionary is
        // instant: the cache holds what the engine said, not what survived the filter, so a new
        // word only changes the filter.
        var engine = new FakeSpellingEngine("Zettlr");
        var dictionary = new FakeUserDictionary();
        var analyzer = new SpellingAnalyzer(engine, dictionary);

        analyzer.Check("Compared with Zettlr here.").ShouldHaveSingleItem();
        int afterFirst = engine.CheckCalls;

        await dictionary.AddAsync("Zettlr", TestContext.Current.CancellationToken);

        analyzer.Check("Compared with Zettlr here.").ShouldBeEmpty();
        engine.CheckCalls.ShouldBe(afterFirst);
    }

    [Fact]
    public void A_line_with_nothing_left_after_masking_never_reaches_the_engine()
    {
        var engine = new FakeSpellingEngine("recieve");
        var analyzer = new SpellingAnalyzer(engine, new FakeUserDictionary());

        analyzer.Check("`recieve`");

        engine.CheckCalls.ShouldBe(0);
    }

    private static IReadOnlyList<SpellingIssue> Check(string text, params string[] wrong) =>
        new SpellingAnalyzer(new FakeSpellingEngine(wrong), new FakeUserDictionary()).Check(text);
}
