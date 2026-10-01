// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>What a review comment on a diagram calls the diagram: the first word its definition says.</summary>
public sealed class DiagramTypeTests
{
    [Theory]
    [InlineData("```mermaid\nflowchart TD\n  A --> B\n```\n", "flowchart")]
    [InlineData("```mermaid\ngraph LR;\n  A --> B\n```\n", "graph")]
    [InlineData("```mermaid\r\nsequenceDiagram\r\n  A->>B: hi\r\n```\r\n", "sequenceDiagram")]
    [InlineData("```mermaid\n\n%% a note\nstateDiagram-v2\n  [*] --> On\n```\n", "stateDiagram-v2")]
    [InlineData("```mermaid\n%%{init: {'theme': 'forest'}}%%\npie\n  \"a\" : 1\n```\n", "pie")]
    public void The_first_word_of_the_definition_is_the_type(string source, string expected)
    {
        DiagramType.At(source, 0).ShouldBe(expected);
    }

    /// <summary>Front matter carries the title and config, ahead of the line that says what it is.</summary>
    [Fact]
    public void Front_matter_is_passed_over()
    {
        const string source = "Intro\n\n```mermaid\n---\ntitle: Orders\n---\nerDiagram\n  A ||--o{ B : has\n```\n";

        DiagramType.At(source, 2).ShouldBe("erDiagram");
    }

    [Theory]
    [InlineData("```mermaid\n```\n", 0)]
    [InlineData("```mermaid\n\n\n```\n", 0)]
    [InlineData("```mermaid\n{weird}\n```\n", 0)]
    [InlineData("```mermaid\nflowchart TD\n```\n", 5)]
    [InlineData("```mermaid\nflowchart TD\n```\n", -3)]
    public void A_definition_that_does_not_say_is_a_diagram(string source, int line)
    {
        DiagramType.At(source, line).ShouldBe(DiagramType.Fallback);
    }
}
