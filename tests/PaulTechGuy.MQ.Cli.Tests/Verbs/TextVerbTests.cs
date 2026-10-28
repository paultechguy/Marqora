// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Editing;
using PaulTechGuy.MQ.Finding;
using PaulTechGuy.MQ.Formatting;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>
/// CLI-VB-FMT-01, NUM-01, UNN-01, RPL-01, REN-01 and DGL-01: each text verb gives exactly what the
/// app's own command gives for the same text and options - checked against a direct call to the
/// library the command calls, on the repository's richest fixture as well as small cases.
/// </summary>
public sealed class TextVerbTests
{
    private const string Ultimate = "docs/UltimateMarkdownContent.md";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> PrintAsync(Workspace workspace, params string[] args)
    {
        CliResult result = await CliRunner.RunAsync(args, currentDirectory: workspace.Root, cancellationToken: Ct);
        result.ExitCode.ShouldBe(0, result.ToString());

        return result.Out;
    }

    [Fact]
    public async Task Format_equals_the_apps_format_document_with_default_rules()
    {
        using var workspace = new Workspace();
        workspace.Copy(Ultimate, "u.md");

        string expected = new MarkdownFormatter().Format(workspace.Read("u.md"), FormatOptions.Default).Text;

        (await PrintAsync(workspace, "format", "u.md")).ShouldBe(expected);
    }

    [Fact]
    public async Task Format_is_idempotent()
    {
        using var workspace = new Workspace();
        workspace.Copy(Ultimate, "u.md");

        (await CliRunner.RunAsync(["format", "--write", "u.md"], currentDirectory: workspace.Root, cancellationToken: Ct)).ExitCode.ShouldBe(0);

        CliResult again = await CliRunner.RunAsync(["format", "--check", "u.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        again.ExitCode.ShouldBe(0, again.ToString());
    }

    [Theory]
    [InlineData("h1", "two-spaces", HeadingNumbering.FromHeading1, HeadingNumberStyle.TwoSpaces)]
    [InlineData("h2", "dot-space", HeadingNumbering.FromHeading2, HeadingNumberStyle.DotSpace)]
    [InlineData("h3", "paren-space", HeadingNumbering.FromHeading3, HeadingNumberStyle.ParenSpace)]
    public async Task Number_equals_the_apps_number_headings(string from, string style, HeadingNumbering start, HeadingNumberStyle numberStyle)
    {
        using var workspace = new Workspace();
        workspace.Copy(Ultimate, "u.md");

        string expected = HeadingNumberRewriter.Apply(workspace.Read("u.md"), start, numberStyle).Text;

        (await PrintAsync(workspace, "number", "--from", from, "--style", style, "u.md")).ShouldBe(expected);
    }

    /// <summary>Without --from, numbering starts where the app's dialog suggests: the level the document already numbers from, then h1.</summary>
    [Fact]
    public async Task Number_without_from_starts_where_the_app_suggests()
    {
        using var workspace = new Workspace();
        workspace.Text("plain.md", "# A\n## B\n");
        workspace.Text("numbered.md", "# Title\n## 1  A\n## 2  B\n### 2.1  C\n");

        (await PrintAsync(workspace, "number", "plain.md")).ShouldBe("# 1  A\n## 1.1  B\n");
        (await PrintAsync(workspace, "number", "numbered.md")).ShouldBe("# Title\n## 1  A\n## 2  B\n### 2.1  C\n");
    }

    [Fact]
    public async Task Number_reports_an_uncertain_heading_with_its_line()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "[go](#scope)\n\n# [Scope](https://example.com)\n");

        CliResult result = await CliRunner.RunAsync(["number", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.Error.ShouldContain("a.md:3: warning:");
        result.Error.ShouldContain("[uncertain-heading]");
    }

    [Fact]
    public async Task Unnumber_equals_the_apps_remove_heading_numbers()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "# 1  A\n## 1.1  B\n[link](#11--b)\n");

        string expected = HeadingNumberRewriter.Remove(workspace.Read("a.md")).Text;

        (await PrintAsync(workspace, "unnumber", "a.md")).ShouldBe(expected);
    }

    [Fact]
    public async Task Number_then_unnumber_returns_a_document_without_numbers_to_itself()
    {
        using var workspace = new Workspace();
        const string Original = "# Guide\n\n[See install](#install)\n\n## Install\n\n## Use\n";
        workspace.Text("a.md", Original);

        (await CliRunner.RunAsync(["number", "--write", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct)).ExitCode.ShouldBe(0);
        (await CliRunner.RunAsync(["unnumber", "--write", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct)).ExitCode.ShouldBe(0);

        workspace.Read("a.md").ShouldBe(Original);
    }

    public static TheoryData<bool, bool, bool> ReplaceOptions
    {
        get
        {
            var data = new TheoryData<bool, bool, bool>();

            foreach (bool matchCase in new[] { false, true })
            {
                foreach (bool wholeWord in new[] { false, true })
                {
                    foreach (bool regex in new[] { false, true })
                    {
                        data.Add(matchCase, wholeWord, regex);
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ReplaceOptions))]
    public async Task Replace_equals_the_apps_replace_all_for_every_option(bool matchCase, bool wholeWord, bool regex)
    {
        using var workspace = new Workspace();
        const string Text = "The Note of notes.\n\n`note` in code, Note again.\n\n```\nnote in a fence\n```\n";
        workspace.Text("a.md", Text);

        string find = regex ? "notes?" : "note";

        var query = new ReplaceQuery
        {
            Find = new FindQuery { Term = find, MatchCase = matchCase, WholeWord = wholeWord, UseRegex = regex },
            Replacement = "hue",
        };

        ReplaceResults direct = DocumentReplacer.Replace(query, [new FindDocument(Guid.NewGuid(), "a.md", "a.md", Text)], Ct);
        string expected = direct.Documents.Count > 0 ? direct.Documents[0].NewText : Text;

        string[] args = ["replace", "--find", find, "--with", "hue", "a.md"];
        args = [.. args, .. matchCase ? ["--match-case"] : Array.Empty<string>(), .. wholeWord ? ["--whole-word"] : Array.Empty<string>(), .. regex ? ["--regex"] : Array.Empty<string>()];

        (await PrintAsync(workspace, args)).ShouldBe(expected);
    }

    [Fact]
    public async Task Replace_with_nothing_deletes_the_matches()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "keep DROP keep\n");

        (await PrintAsync(workspace, "replace", "--find", "DROP ", "--with", "", "a.md")).ShouldBe("keep keep\n");
    }

    [Fact]
    public async Task With_equals_and_nothing_after_it_deletes_the_matches()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "keep DROP keep\n");

        // The parser read "--with=" as no value and took the next word, so this printed the
        // document with "--dry-run" in place of each match instead of showing a diff.
        CliResult result = await CliRunner.RunAsync(["replace", "--find", "DROP ", "--with=", "--dry-run", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.Out.ShouldContain("+keep keep");
        result.Out.ShouldNotContain("--dry-run");
        workspace.Read("a.md").ShouldBe("keep DROP keep\n");
    }

    [Fact]
    public async Task An_option_after_an_option_that_needs_a_value_exits_2_before_any_file_is_read()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "keep DROP keep\n");

        // What Windows PowerShell 5.1 passes for --with '' --write: it drops the empty word.
        CliResult result = await CliRunner.RunAsync(["replace", "--find", "DROP ", "--with", "--write", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(2, result.ToString());
        result.Out.ShouldBeEmpty();
        result.Error.ShouldContain("'--with' needs a value, but the next word is the option '--write'.");
        result.Error.ShouldContain("--with=--write");
        workspace.Read("a.md").ShouldBe("keep DROP keep\n");
    }

    [Fact]
    public async Task An_option_name_joined_with_equals_is_taken_as_the_text()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "keep DROP keep\n");

        (await PrintAsync(workspace, "replace", "--find", "DROP", "--with=--write", "a.md")).ShouldBe("keep --write keep\n");
    }

    [Fact]
    public async Task An_invalid_regex_exits_2_with_the_parsers_message_before_any_file_is_read()
    {
        CliResult result = await CliRunner.RunAsync(["replace", "--find", "(", "--with", "x", "--regex", "missing.md"], cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("not a valid regular expression");
        result.Error.ShouldNotContain("missing.md: No such file");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sequential")]
    [InlineData("ones")]
    public async Task Renumber_lists_equals_the_library_pass(string? style)
    {
        using var workspace = new Workspace();
        workspace.Copy(Ultimate, "u.md");

        ListNumbering? numbering = style switch { "sequential" => ListNumbering.Sequential, "ones" => ListNumbering.Ones, _ => null };
        string expected = DocumentLists.RenumberAll(workspace.Read("u.md"), numbering).Text;

        string[] args = style is null ? ["renumber-lists", "u.md"] : ["renumber-lists", "--style", style, "u.md"];
        string printed = await PrintAsync(workspace, args);

        // The library joins with LF; the CLI puts the file's own endings and final newline back.
        printed.ReplaceLineEndings("\n").TrimEnd('\n').ShouldBe(expected.TrimEnd('\n'));
    }

    [Fact]
    public async Task Renumber_lists_names_a_list_that_cannot_take_zeros()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "Intro\n1. a\n2. b\n");

        CliResult result = await CliRunner.RunAsync(["renumber-lists", "--style", "zeros", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.Out.ShouldBe("Intro\n1. a\n2. b\n");
        result.Error.ShouldContain("a.md:2: warning:");
        result.Error.ShouldContain("[list-left-alone]");
    }

    private const string Diagrams =
        "```mermaid\nflowchart TB\n  A-->B\n```\n\n" +
        "```mermaid\nsequenceDiagram\n  A->>B: hi\n```\n\n" +
        "```mermaid\nstateDiagram-v2\n  [*] --> S\n```\n";

    [Fact]
    public async Task Diagram_layout_equals_edit_for_applied_to_each_diagram()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", Diagrams);

        List<string> lines = [.. Diagrams.Split('\n')];
        IReadOnlyList<(int Open, int Close)> fences = DiagramLayout.Fences(lines);

        for (int i = fences.Count - 1; i >= 0; i--)
        {
            if (DiagramLayout.EditFor(lines, fences[i].Open, fences[i].Close, DiagramDirection.LeftToRight) is { } edit)
            {
                if (edit.InsertsLine)
                {
                    lines.Insert(edit.Line + 1, edit.Text);
                }
                else
                {
                    lines[edit.Line] = lines[edit.Line][..edit.Start] + edit.Text + lines[edit.Line][edit.End..];
                }
            }
        }

        (await PrintAsync(workspace, "diagram-layout", "--direction", "left-to-right", "a.md")).ShouldBe(string.Join('\n', lines));
    }

    [Fact]
    public async Task Diagram_layout_on_one_diagram_touches_only_that_one()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", Diagrams);

        string printed = await PrintAsync(workspace, "diagram-layout", "--direction", "left-to-right", "--diagram", "3", "a.md");

        printed.ShouldContain("flowchart TB");
        printed.ShouldContain("direction LR");
    }

    [Fact]
    public async Task A_diagram_with_no_direction_is_left_alone_and_named()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", Diagrams);

        CliResult result = await CliRunner.RunAsync(["diagram-layout", "--direction", "left-to-right", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.Out.ShouldContain("sequenceDiagram\n  A->>B: hi\n");
        result.Error.ShouldContain("a.md:6: info: Diagram 2 is a kind with no direction to set");
    }
}
