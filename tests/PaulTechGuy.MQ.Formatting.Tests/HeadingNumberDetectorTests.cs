// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Formatting.Tests;

public class HeadingNumberDetectorTests
{
    private static HeadingNumberDetector.Scan Read(string document) =>
        HeadingNumberDetector.Read(document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));

    // ------------------------------------------------------------ recognizing

    [Fact]
    public void A_document_that_numbers_itself_is_recognized()
    {
        HeadingNumberDetector.Scan scan = Read(
            """
            # 1 Introduction
            ## 1.1 Purpose
            ## 1.2 Scope
            # 2 Method
            """);

        scan.IsNumbered.ShouldBeTrue();
        scan.StartLevel.ShouldBe(HeadingNumbering.FromHeading1);
        scan.NumberedCount.ShouldBe(4);
        scan.Numbered.ShouldBe([true, true, true, true]);
    }

    [Fact]
    public void The_start_level_falls_out_of_the_comparison()
    {
        // The title is unnumbered and the count begins at "##", which is how a document with a
        // single top-level title is usually written.
        HeadingNumberDetector.Scan scan = Read(
            """
            # Specification
            ## 1 Introduction
            ## 2 Scope
            ### 2.1 In scope
            """);

        scan.StartLevel.ShouldBe(HeadingNumbering.FromHeading2);
        scan.Numbered.ShouldBe([false, true, true, true]);
    }

    [Fact]
    public void A_document_numbering_only_its_top_levels_is_recognized()
    {
        // Numbering the chapters and sections but not the sub-sections is how most numbered
        // documents are actually written, so the unnumbered depths must not outvote the rest.
        HeadingNumberDetector.Scan scan = Read(
            """
            # 1 Alpha
            ## 1.1 One
            ### Deep
            ### Deeper
            # 2 Beta
            ## 2.1 Two
            ### Deep again
            """);

        scan.StartLevel.ShouldBe(HeadingNumbering.FromHeading1);
        scan.NumberedCount.ShouldBe(4);
        scan.Numbered.ShouldBe([true, true, false, false, true, true, false]);
    }

    [Fact]
    public void A_document_that_skips_a_level_is_recognized()
    {
        // The "###" here has no "##" above it, and the counter writes that gap as a zero -
        // "1.0.1", not "1.1", because only a leading zero is dropped. That happens to be the
        // form a CSS counter chain and Word write too, so an imported document matches without
        // the detector needing a second opinion about padding.
        HeadingNumberDetector.Scan scan = Read("# 1 Alpha\n### 1.0.1 Deep");

        scan.StartLevel.ShouldBe(HeadingNumbering.FromHeading1);
        scan.Numbered.ShouldBe([true, true]);
    }

    [Fact]
    public void A_numbered_document_with_a_section_inserted_is_still_recognized()
    {
        // Every number below the insert is now one out, so reading the document straight agrees
        // with almost nothing. Stepping over the unnumbered heading still reads 1, 2.
        //
        // This is the state a document is actually in when someone reaches for Number Headings,
        // and getting it wrong is expensive: an unrecognized document is not renumbered but
        // prefixed, so "1  Alpha" would become "1  1  Alpha".
        HeadingNumberDetector.Scan scan = Read(
            """
            # 1  Alpha
            # Inserted
            # 2  Beta
            """);

        scan.IsNumbered.ShouldBeTrue();
        scan.StartLevel.ShouldBe(HeadingNumbering.FromHeading1);
        scan.Numbered.ShouldBe([true, false, true]);
    }

    [Fact]
    public void An_unnumbered_title_over_numbered_sections_still_counts_from_heading_two()
    {
        // The reading that steps over unnumbered headings could drop the title and call these a
        // top-level count, because a dropped leading zero makes the two look identical. Reading
        // the document straight first is what stops it.
        Read(
            """
            # Specification
            ## 1 Introduction
            ## 2 Scope
            ### 2.1 In scope
            #### Detail
            """).StartLevel.ShouldBe(HeadingNumbering.FromHeading2);
    }

    [Fact]
    public void Sections_numbered_under_unnumbered_parts_count_from_heading_two()
    {
        // A real document's shape: unnumbered "#" parts, with the "##" sections numbered right
        // through them rather than restarting at each part. Reading it straight agrees with only
        // the first few, so the count has to be read with the parts stepped over - and once they
        // are gone the sections look exactly like a top-level count, because a leading zero is
        // dropped and both produce 1, 2, 3.
        //
        // The shallowest numbered heading is a "##", so that is where the count starts. Getting
        // this wrong sends the renumbering dialog to Heading 1 and pushes the whole document
        // down a level from what the preview shows.
        HeadingNumberDetector.Scan scan = Read(
            """
            # Title

            # Part I

            ## 1. What it is
            ## 2. Principles

            # Part II

            ## 3. Anatomy
            ## 4. By situation

            ### 4.1 On a topic
            ### 4.2 On a dashboard
            """);

        scan.IsNumbered.ShouldBeTrue();
        scan.StartLevel.ShouldBe(HeadingNumbering.FromHeading2);
        scan.NumberedCount.ShouldBe(6);
    }

    [Fact]
    public void Sub_sections_numbered_within_a_named_parent_are_recognized()
    {
        // The front of each number is the parent's name, written in words, so no count agrees:
        // from "##" these are 3.1 and 4.1, from "###" they are 1, 2, 3. The unnumbered sibling
        // after "1.2" is stepped over rather than breaking the run.
        HeadingNumberDetector.Scan scan = Read(
            """
            # Migration
            ## Where we are
            ## The strategy
            ### Why switch
            ## Phase 1 — Prep
            ### 1.1 Pipeline routing
            ### 1.2 Cut Release workflow
            ### Prep exit criteria
            ## Phase 2 — Cutover
            ### 2.1 Freeze dev
            ### 2.2 Final sync
            ### 2.3 Smoke test
            """);

        scan.IsNumbered.ShouldBeTrue();
        scan.StartLevel.ShouldBe(HeadingNumbering.FromHeading2);
        scan.NumberedCount.ShouldBe(5);
        scan.Numbered.ShouldBe([false, false, false, false, false, true, true, false, false, true, true, true]);
    }

    [Fact]
    public void A_count_that_agrees_is_preferred_to_runs_within_parents()
    {
        // These also share a front and count from one under each parent, but the document's own
        // counter explains them, and that reading is the one that sets the start level.
        HeadingNumberDetector.Scan scan = Read("# 1 Alpha\n## 1.1 One\n## 1.2 Two\n# 2 Beta\n## 2.1 Three");

        scan.StartLevel.ShouldBe(HeadingNumbering.FromHeading1);
        scan.NumberedCount.ShouldBe(5);
    }

    // -------------------------------------------------------------- rejecting

    [Fact]
    public void A_run_within_a_parent_must_start_at_one()
    {
        Read(
            """
            ## Releases
            ### 2.4 Spring
            ### 2.5 Summer
            ### 2.6 Autumn
            """).IsNumbered.ShouldBeFalse();
    }

    [Fact]
    public void A_run_within_a_parent_keeps_one_front()
    {
        // 1.1 then 2.2 under the same parent is not one parent's sections.
        Read(
            """
            ## Notes
            ### 1.1 First
            ### 2.2 Second
            ### 3.3 Third
            """).IsNumbered.ShouldBeFalse();
    }

    [Fact]
    public void A_year_heading_on_its_own_is_not_numbering()
    {
        Read("# 2026 Budget\n## Overview").IsNumbered.ShouldBeFalse();
    }

    [Fact]
    public void A_perfect_run_that_does_not_start_at_one_is_not_numbering()
    {
        // 2026, 2027, 2028 is a flawless increment and still three years. The counter starts at
        // one, so comparing against it is what rejects them.
        HeadingNumberDetector.Scan scan = Read(
            """
            # 2026 Budget
            # 2027 Budget
            # 2028 Budget
            """);

        scan.IsNumbered.ShouldBeFalse();
        scan.StartLevel.ShouldBe(HeadingNumbering.Off);
        scan.Numbered.ShouldBe([false, false, false]);
    }

    [Fact]
    public void A_version_history_is_not_numbering()
    {
        Read(
            """
            # Changelog
            ## 1.0.0 First release
            ## 1.1.0 Second release
            ## 2.0.0 Third release
            """).IsNumbered.ShouldBeFalse();
    }

    [Fact]
    public void One_agreeing_heading_is_not_enough()
    {
        // A lone "1." agrees with the counter perfectly, and reading it as a scheme would stand
        // Marqora's numbers down across a document that has none.
        Read("# 1. Overview\n# Details\n# More").IsNumbered.ShouldBeFalse();
    }

    [Fact]
    public void An_unnumbered_document_is_left_alone()
    {
        HeadingNumberDetector.Scan scan = Read("# Introduction\n## Purpose\n## Scope");

        scan.IsNumbered.ShouldBeFalse();
        scan.Headings.Count.ShouldBe(3);
        scan.Numbered.ShouldBe([false, false, false]);
    }

    [Fact]
    public void A_document_with_no_headings_is_not_numbered()
    {
        HeadingNumberDetector.Scan scan = Read("Just a paragraph.\n\nAnd another.");

        scan.IsNumbered.ShouldBeFalse();
        scan.Headings.ShouldBeEmpty();
        scan.Numbered.ShouldBeEmpty();
    }

    // ------------------------------------------------------- one at a time

    [Fact]
    public void A_year_inside_a_numbered_document_keeps_its_number()
    {
        // The document is numbered, but this one heading did not write the number the counter
        // would have given it, so it is a year sitting in a numbered document rather than a
        // section number - and a rewriter must leave it exactly as it is.
        HeadingNumberDetector.Scan scan = Read(
            """
            # 1 Introduction
            # 2 Scope
            # 2026 Budget
            # 4 Method
            """);

        scan.IsNumbered.ShouldBeTrue();
        scan.Numbered.ShouldBe([true, true, false, true]);
        scan.Headings[2].Title.ShouldBe("Budget");
    }

    [Fact]
    public void The_judgment_stays_index_for_index_with_the_headings()
    {
        HeadingNumberDetector.Scan scan = Read("# 1 Alpha\n# Untouched\n# 3 Gamma");

        scan.Numbered.Count.ShouldBe(scan.Headings.Count);
        scan.Numbered.ShouldBe([true, false, true]);
    }

    // ---------------------------------------------------------------- source

    [Fact]
    public void Numbers_inside_a_fence_are_not_counted()
    {
        // With the fence read as a heading the count would run 1, 2, 3 against a document that
        // wrote 1, 9, 2, and the document would come back unnumbered.
        HeadingNumberDetector.Scan scan = Read(
            """
            # 1 Alpha

            ```text
            # 9 Beta
            ```

            # 2 Gamma
            """);

        scan.Headings.Count.ShouldBe(2);
        scan.IsNumbered.ShouldBeTrue();
        scan.NumberedCount.ShouldBe(2);
    }
}
