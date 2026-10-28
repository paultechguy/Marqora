// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Run;

/// <summary>
/// CLI-RPT-04 and CLI-RPT-05: every --json line parses and carries schemaVersion 1, each verb's
/// lines match a recorded golden file, and the shape of a line matches the recorded version-1
/// schema, so a field renamed or removed fails here before it fails a user's script.
/// </summary>
public sealed class JsonGoldenTests
{
    private const string Document =
        "# Guide\n\n## Install\n\n1. one\n2. two\n\n```mermaid\nsequenceDiagram\n  A->>B: hi\n```\n\n```mermaid\nflowchart TB\n  A-->B\n```\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Verbs => new()
    {
        { "format", "format --check --json {0}" },
        { "number", "number --dry-run --json {0}" },
        { "unnumber", "unnumber --json {0}" },
        { "replace", "replace --find one --with ONE --json {0}" },
        { "renumber-lists", "renumber-lists --check --json {0}" },
        { "diagram-layout", "diagram-layout --direction left-to-right --check --json {0}" },
        { "missing", "number --json missing.md" },
    };

    [Theory]
    [MemberData(nameof(Verbs))]
    public async Task Each_verbs_json_matches_its_golden_file(string name, string commandLine)
    {
        using var workspace = new Workspace();
        workspace.Text("guide.md", Document);

        CliResult result = await CliRunner.RunAsync(
            string.Format(System.Globalization.CultureInfo.InvariantCulture, commandLine, "guide.md").Split(' '),
            currentDirectory: workspace.Root,
            cancellationToken: Ct);

        foreach (string line in result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using JsonDocument parsed = JsonDocument.Parse(line);
            parsed.RootElement.GetProperty("schemaVersion").GetInt32().ShouldBe(1);
        }

        Golden.Matches($"json/{name}.jsonl", result.Out);
    }

    /// <summary>
    /// The properties a line can carry, by their JSON names, recorded once. Adding one means
    /// re-recording this file - which is allowed and keeps the version. Removing or renaming one
    /// makes this fail, and means raising the version instead.
    /// </summary>
    [Fact]
    public void The_shape_of_a_line_is_the_recorded_version_1_schema()
    {
        string[] names = [.. typeof(JsonLine).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .Order(StringComparer.Ordinal)];

        string[] findings = [.. typeof(DocumentFinding).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => "findings." + JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .Order(StringComparer.Ordinal)];

        JsonLine.CurrentSchemaVersion.ShouldBe(1);
        Golden.Matches("json/schema-v1.txt", string.Join('\n', names.Concat(findings)) + "\n");
    }

    [Fact]
    public async Task A_partial_failure_shows_per_file_in_json_alone()
    {
        using var workspace = new Workspace();
        workspace.Text("good.md", "# A\n");

        CliResult result = await CliRunner.RunAsync(["number", "--check", "--json", "good.md", "missing.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        string[] lines = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(2);
        lines.ShouldContain(line => line.Contains("\"path\":\"missing.md\"") && line.Contains("\"outcome\":\"failed\""));
        lines.ShouldContain(line => line.Contains("\"path\":\"good.md\"") && line.Contains("\"outcome\":\"wouldChange\""));
    }
}
