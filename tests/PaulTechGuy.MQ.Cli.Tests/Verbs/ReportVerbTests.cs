// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Analysis;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Rendering;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>CLI-VB-CHK-01, CHK-02, TOC-01 and OUT-01: the verbs that read a document and report on it.</summary>
public sealed class ReportVerbTests
{
    private static readonly MarkdigMarkdownRenderer Renderer = new(NullLogger<MarkdigMarkdownRenderer>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Broken =
        "# Guide\n\n" +
        "See [install](#instal) for setup.\n\n" +
        "![Diagram](images/missing.png)\n\n" +
        "Read [the notes](notes/missing.md).\n";

    [Fact]
    public async Task Check_finds_a_dead_anchor_a_missing_image_and_a_dead_relative_link()
    {
        using var workspace = new Workspace();
        workspace.Text("guide.md", Broken);

        CliResult result = await CliRunner.RunAsync(["check", "guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(1);
        result.Out.ShouldContain("guide.md:3:5: error:");
        result.Out.ShouldContain("[dead-anchor]");
        result.Out.ShouldContain("guide.md:5:1: error:");
        result.Out.ShouldContain("[missing-image]");
        result.Out.ShouldContain("guide.md:7:6: error:");
        result.Out.ShouldContain("[broken-link]");
    }

    /// <summary>The findings are the analyzer's own for the request the app builds: what the squiggles show.</summary>
    [Fact]
    public async Task Check_findings_equal_the_analyzers_for_the_same_document()
    {
        using var workspace = new Workspace();
        string path = workspace.Copy("docs/UltimateMarkdownContent.md", "u.md");
        string text = workspace.Read("u.md");

        RenderedMarkdown rendered = Renderer.Render(text);
        AnalysisResult direct = new MarkdownAnalyzer().Analyze(new AnalysisRequest
        {
            Text = text,
            DocumentPath = path,
            Links = rendered.Links,
            Outline = rendered.Outline,
            Anchors = rendered.Anchors,
            CheckImageAltText = false,
            CheckBlockedImages = true,
        });

        CliResult result = await CliRunner.RunAsync(["check", "--json", "u.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        foreach (LinkFinding finding in direct.LinkFindings)
        {
            result.Out.ShouldContain($"\"line\":{finding.Line + 1},\"column\":{finding.Start + 1}");
        }
    }

    [Fact]
    public async Task A_web_image_fails_the_run_by_default()
    {
        using var workspace = new Workspace();
        workspace.Text("README.md", "# Project\n\n[![build](https://example.com/badge.svg)](https://example.com/ci)\n");

        CliResult result = await CliRunner.RunAsync(["check", "README.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(1);
        result.Out.ShouldContain("[remote-image]");
    }

    /// <summary>Amendment A13b: the flag turns web and outside pictures into information, shown only with --verbose.</summary>
    [Fact]
    public async Task Ignore_external_images_passes_and_verbose_still_lists_them()
    {
        using var workspace = new Workspace();
        workspace.Text("README.md", "# Project\n\n[![build](https://example.com/badge.svg)](https://example.com/ci)\n");

        CliResult quietly = await CliRunner.RunAsync(["check", "--ignore-external-images", "README.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        CliResult verbosely = await CliRunner.RunAsync(["check", "--ignore-external-images", "--verbose", "README.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        quietly.ExitCode.ShouldBe(0);
        quietly.Out.ShouldBeEmpty();
        verbosely.ExitCode.ShouldBe(0);
        verbosely.Out.ShouldContain("info:");
        verbosely.Out.ShouldContain("[remote-image]");
    }

    [Fact]
    public async Task A_web_link_is_never_contacted_and_verbose_lists_it_as_not_checked()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "See [the site](https://example.com/page).\n");

        CliResult result = await CliRunner.RunAsync(["check", "--verbose", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0);
        result.Out.ShouldContain("[web-link-not-checked]");
    }

    [Fact]
    public async Task Standard_input_with_a_base_folder_finds_what_the_file_does()
    {
        using var workspace = new Workspace();
        workspace.Text("docs/x.md", Broken);

        CliResult fromFile = await CliRunner.RunAsync(["check", "docs/x.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        CliResult fromStdin = await CliRunner.RunAsync(
            ["check", "--base-dir", "docs", "-"],
            currentDirectory: workspace.Root,
            standardInput: System.Text.Encoding.UTF8.GetBytes(Broken),
            cancellationToken: Ct);

        fromStdin.ExitCode.ShouldBe(fromFile.ExitCode);
        fromStdin.Out.Replace("-:", "docs/x.md:", StringComparison.Ordinal).Replace('\\', '/').ShouldBe(fromFile.Out.Replace('\\', '/'));
    }

    /// <summary>A style hint fails the run only with --strict; --strict without --style is a usage error (tested with the usage errors).</summary>
    [Fact]
    public async Task A_style_only_problem_exits_0_with_style_and_1_with_strict()
    {
        using var workspace = new Workspace();
        workspace.Bytes("pic.png", [0x89, 0x50, 0x4E, 0x47]);
        workspace.Text("a.md", "# Title\n\n![](pic.png)\n");

        CliResult plain = await CliRunner.RunAsync(["check", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        CliResult styled = await CliRunner.RunAsync(["check", "--style", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        CliResult strict = await CliRunner.RunAsync(["check", "--style", "--strict", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        plain.ExitCode.ShouldBe(0);
        plain.Out.ShouldBeEmpty();
        styled.ExitCode.ShouldBe(0);
        styled.Out.ShouldContain("hint:");
        strict.ExitCode.ShouldBe(1);
    }

    [Fact]
    public async Task Every_toc_link_lands_on_a_heading_in_the_rendered_document()
    {
        using var workspace = new Workspace();
        workspace.Copy("webshell/cheatsheet.md", "c.md");

        CliResult result = await CliRunner.RunAsync(["toc", "c.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        string html = Renderer.Render(workspace.Read("c.md")).Html;

        result.ExitCode.ShouldBe(0);
        string[] anchors = [.. result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line[(line.LastIndexOf("](#", StringComparison.Ordinal) + 3)..^1])];

        anchors.ShouldNotBeEmpty();

        foreach (string anchor in anchors)
        {
            html.ShouldContain($"id=\"{anchor}\"");
        }
    }

    [Fact]
    public async Task Outline_lists_the_headings_the_outline_pane_shows()
    {
        using var workspace = new Workspace();
        workspace.Copy("webshell/cheatsheet.md", "c.md");

        IReadOnlyList<OutlineHeading> outline = Renderer.Render(workspace.Read("c.md")).Outline;
        CliResult result = await CliRunner.RunAsync(["outline", "c.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        string[] lines = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines.Length.ShouldBe(outline.Count);

        for (int i = 0; i < outline.Count; i++)
        {
            lines[i].TrimStart().ShouldStartWith($"{outline[i].SourceLine + 1}  ");
            lines[i].ShouldEndWith(outline[i].Text);
        }
    }

    [Fact]
    public async Task Outline_json_carries_level_text_anchor_and_line()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "# Title\n\n## Install\n");

        CliResult result = await CliRunner.RunAsync(["outline", "--json", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.Out.ShouldContain("\"headings\":[{\"level\":1,\"text\":\"Title\",\"anchor\":\"title\",\"line\":1},{\"level\":2,\"text\":\"Install\",\"anchor\":\"install\",\"line\":3}]");
    }
}
