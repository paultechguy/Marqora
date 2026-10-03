// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// Reading and rewriting which way a mermaid diagram is drawn. Each test applies the edit the
/// way the source pane and the Format menu both do, and checks the text that comes out: the edit's
/// numbers are only right if the document they produce is.
/// </summary>
public sealed class DiagramLayoutTests
{
    private static string[] Lines(string source) => source.Split('\n');

    /// <summary>The fence's diagram turned to <paramref name="want"/>, or the source unchanged.</summary>
    private static string Turn(string source, DiagramDirection want)
    {
        string[] lines = Lines(source);
        (int open, int close) = DiagramLayout.Fences(lines).Single();

        if (DiagramLayout.EditFor(lines, open, close, want) is not { } edit)
        {
            return source;
        }

        var result = lines.ToList();

        if (edit.InsertsLine)
        {
            result.Insert(edit.Line + 1, edit.Text);
        }
        else
        {
            string text = result[edit.Line];
            result[edit.Line] = text[..edit.Start] + edit.Text + text[edit.End..];
        }

        return string.Join("\n", result);
    }

    private static DiagramDirection? Current(string source)
    {
        string[] lines = Lines(source);
        (int open, int close) = DiagramLayout.Fences(lines).Single();

        return DiagramLayout.Current(lines, open, close);
    }

    [Theory]
    [InlineData("```mermaid\nflowchart LR\n  A --> B\n```", "```mermaid\nflowchart TB\n  A --> B\n```")]
    [InlineData("```mermaid\ngraph LR;\n  A --> B\n```", "```mermaid\ngraph TB;\n  A --> B\n```")]
    [InlineData("```mermaid\nflowchart >\n  A --> B\n```", "```mermaid\nflowchart TB\n  A --> B\n```")]
    [InlineData("```mermaid\ntimeline LR\n  2024 : a\n```", "```mermaid\ntimeline TD\n  2024 : a\n```")]
    [InlineData("```mermaid\ntimeline\n  2024 : a\n```", "```mermaid\ntimeline TD\n  2024 : a\n```")]
    [InlineData("```mermaid\ngitGraph\n  commit\n```", "```mermaid\ngitGraph TB:\n  commit\n```")]
    [InlineData("```mermaid\ngitGraph LR:\n  commit\n```", "```mermaid\ngitGraph TB:\n  commit\n```")]
    public void Top_to_bottom_is_written_where_the_type_keeps_it(string source, string expected)
    {
        Turn(source, DiagramDirection.TopToBottom).ShouldBe(expected);
    }

    [Theory]
    [InlineData("```mermaid\nflowchart TD\n  A --> B\n```", "```mermaid\nflowchart LR\n  A --> B\n```")]
    [InlineData("```mermaid\ngraph\n  A --> B\n```", "```mermaid\ngraph LR\n  A --> B\n```")]
    [InlineData("```mermaid\ngraph;\n  A --> B\n```", "```mermaid\ngraph LR;\n  A --> B\n```")]
    [InlineData("```mermaid\ngitGraph TB:\n  commit\n```", "```mermaid\ngitGraph LR:\n  commit\n```")]
    [InlineData(
        "```mermaid\nstateDiagram-v2\n    direction TB\n    [*] --> On\n```",
        "```mermaid\nstateDiagram-v2\n    direction LR\n    [*] --> On\n```")]
    public void Left_to_right_replaces_what_is_there(string source, string expected)
    {
        Turn(source, DiagramDirection.LeftToRight).ShouldBe(expected);
    }

    /// <summary>A type that keeps its direction on a line of its own gets one, lined up with the body.</summary>
    [Theory]
    [InlineData("```mermaid\nclassDiagram\n  class A\n```", "```mermaid\nclassDiagram\n  direction LR\n  class A\n```")]
    [InlineData("```mermaid\nerDiagram\n    A ||--o{ B : has\n```", "```mermaid\nerDiagram\n    direction LR\n    A ||--o{ B : has\n```")]
    [InlineData("```mermaid\nrequirementDiagram\n```", "```mermaid\nrequirementDiagram\n    direction LR\n```")]
    public void A_missing_direction_statement_is_added(string source, string expected)
    {
        Turn(source, DiagramDirection.LeftToRight).ShouldBe(expected);
    }

    /// <summary>A composite state's own direction is its own; the diagram's goes in at the top.</summary>
    [Fact]
    public void A_nested_direction_is_left_alone()
    {
        const string source =
            "```mermaid\nstateDiagram-v2\n  state Busy {\n    direction LR\n    a --> b\n  }\n```";

        Current(source).ShouldBe(DiagramDirection.TopToBottom);
        Turn(source, DiagramDirection.LeftToRight).ShouldBe(
            "```mermaid\nstateDiagram-v2\n  direction LR\n  state Busy {\n    direction LR\n    a --> b\n  }\n```");
    }

    [Fact]
    public void Front_matter_and_init_lines_are_passed_over()
    {
        const string source =
            "```mermaid\n---\ntitle: Flow\n---\n%%{init: {'theme': 'forest'}}%%\nflowchart LR\n  A --> B\n```";

        Turn(source, DiagramDirection.TopToBottom).ShouldBe(
            "```mermaid\n---\ntitle: Flow\n---\n%%{init: {'theme': 'forest'}}%%\nflowchart TB\n  A --> B\n```");
    }

    [Theory]
    [InlineData("```mermaid\nflowchart TD\n```", DiagramDirection.TopToBottom)]
    [InlineData("```mermaid\nflowchart\n```", DiagramDirection.TopToBottom)]
    [InlineData("```mermaid\nflowchart RL\n```", DiagramDirection.AsIs)]
    [InlineData("```mermaid\ngraph bt\n```", DiagramDirection.AsIs)]
    [InlineData("```mermaid\ngitGraph\n```", DiagramDirection.LeftToRight)]
    [InlineData("```mermaid\ntimeline\n```", DiagramDirection.LeftToRight)]
    [InlineData("```mermaid\nclassDiagram\n  direction LR\n```", DiagramDirection.LeftToRight)]
    public void The_current_direction_counts_the_default_when_nothing_is_written(
        string source, DiagramDirection expected)
    {
        Current(source).ShouldBe(expected);
    }

    [Theory]
    [InlineData("```mermaid\nsequenceDiagram\n  A->>B: hi\n```")]
    [InlineData("```mermaid\ngantt\n  title Plan\n```")]
    [InlineData("```mermaid\nmindmap\n  root\n```")]
    [InlineData("```mermaid\nC4Context\n  title x\n```")]
    [InlineData("```mermaid\nFlowchart LR\n```")]
    [InlineData("```mermaid\n```")]
    public void A_type_with_no_direction_offers_nothing(string source)
    {
        Current(source).ShouldBeNull();
        Turn(source, DiagramDirection.TopToBottom).ShouldBe(source);
    }

    [Fact]
    public void Already_running_that_way_needs_no_edit()
    {
        string[] lines = Lines("```mermaid\nflowchart TD\n```");

        DiagramLayout.EditFor(lines, 0, 2, DiagramDirection.TopToBottom).ShouldBeNull();
        DiagramLayout.EditFor(lines, 0, 2, DiagramDirection.AsIs).ShouldBeNull();
    }

    [Fact]
    public void A_carriage_return_stays_at_the_end_of_the_line()
    {
        Turn("```mermaid\r\nflowchart\r\n  A --> B\r\n```", DiagramDirection.LeftToRight)
            .ShouldBe("```mermaid\r\nflowchart LR\r\n  A --> B\r\n```");
    }

    [Fact]
    public void Only_mermaid_fences_are_found_and_each_holds_its_own_lines()
    {
        string[] lines = Lines(
            "```js\nflowchart LR\n```\n\n~~~~ Mermaid title\nflowchart LR\n```\n~~~~\n```mermaid\ngraph TD");

        DiagramLayout.Fences(lines).ShouldBe([(4, 7), (8, 10)]);
        DiagramLayout.FenceAt(lines, 6).ShouldBe((4, 7));
        DiagramLayout.FenceAt(lines, 1).ShouldBeNull();
    }
}
