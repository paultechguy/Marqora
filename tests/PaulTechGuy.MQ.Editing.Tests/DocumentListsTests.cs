// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Editing.Tests;

/// <summary>
/// Renumber List run on every list in a document: the same as the command run list by list with
/// the caret on each list's first line, which is what marqora-cli's renumber-lists promises.
/// </summary>
public sealed class DocumentListsTests
{
    private const string Lists =
        "# Steps\n\n" +
        "1. one\n" +
        "2. two\n" +
        "3. three\n\n" +
        "Some text.\n\n" +
        "1. again\n" +
        "1. again\n" +
        "   1. nested\n" +
        "   2. nested\n" +
        "1. again\n";

    /// <summary>The first lines of the three lists above, outer and nested, zero-based.</summary>
    private static readonly int[] Starts = [2, 8, 10];

    [Theory]
    [InlineData(ListNumbering.Sequential)]
    [InlineData(ListNumbering.Ones)]
    [InlineData(null)]
    public void Equals_renumber_list_applied_to_each_list_in_turn(ListNumbering? numbering)
    {
        ListsRenumbered all = DocumentLists.RenumberAll(Lists, numbering);

        all.Text.ShouldBe(ByHand(Lists, numbering));
    }

    [Fact]
    public void Without_a_numbering_each_list_takes_its_own_suggestion()
    {
        ListsRenumbered result = DocumentLists.RenumberAll(Lists, null);

        // The counting list becomes the shorthand; the shorthand is counted up; the nested list
        // counted up becomes the shorthand. Each list's own suggestion, not one rule for all.
        result.Text.ShouldContain("1. one\n1. two\n1. three");
        result.Text.ShouldContain("1. again\n2. again\n");
        result.Text.ShouldContain("   1. nested\n   1. nested");
        result.Changed.ShouldBe(3);
    }

    [Fact]
    public void A_list_that_cannot_start_at_zero_is_left_alone_and_named()
    {
        ListsRenumbered result = DocumentLists.RenumberAll("Intro text\n1. a\n2. b\n", ListNumbering.Zeros);

        result.Text.ShouldBe("Intro text\n1. a\n2. b\n");
        result.LeftAlone.ShouldBe([2]);
        result.Changed.ShouldBe(0);
    }

    [Fact]
    public void A_document_with_no_lists_comes_back_unchanged()
    {
        ListsRenumbered result = DocumentLists.RenumberAll("# Title\n\nJust prose.\n", ListNumbering.Sequential);

        result.Text.ShouldBe("# Title\n\nJust prose.\n");
        result.Changed.ShouldBe(0);
    }

    /// <summary>The app's own path: the editor's Renumber List, with the caret on each list's first line in turn.</summary>
    private static string ByHand(string markdown, ListNumbering? numbering)
    {
        var editor = new MarkdownEditor();
        List<string> lines = [.. markdown.Split('\n')];

        foreach (int start in Starts)
        {
            var context = new EditContext(lines, 0, TextRange.At(new TextPosition(start, 0)));
            OrderedListSummary summary = editor.DescribeOrderedList(context)!;
            EditResult result = editor.RenumberList(numbering ?? summary.Suggested, context);

            foreach (TextEdit edit in result.Edits.OrderByDescending(e => e.Range.Start.Line).ThenByDescending(e => e.Range.Start.Column))
            {
                string text = lines[edit.Range.Start.Line];
                lines[edit.Range.Start.Line] = text[..edit.Range.Start.Column] + edit.Text + text[edit.Range.End.Column..];
            }
        }

        return string.Join('\n', lines);
    }
}
