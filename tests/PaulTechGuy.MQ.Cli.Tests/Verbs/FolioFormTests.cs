// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Cli.Verbs;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>
/// CLI-FO-01 and CLI-FO-02: the form comes from the output path, --form decides a path that says
/// nothing, and the two never disagree quietly. The single page is made by a real browser, so the
/// class runs with the other browser tests, one at a time.
/// </summary>
[Collection(nameof(BrowserRunsAlone))]
public sealed class FolioFormTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Handbook.zip", null, FolioForm.Zip)]
    [InlineData("Handbook.html", null, FolioForm.SingleFile)]
    [InlineData("Handbook.htm", null, FolioForm.SingleFile)]
    [InlineData("Handbook/", null, FolioForm.Folder)]
    [InlineData("Handbook\\", null, FolioForm.Folder)]
    [InlineData("Handbook", "folder", FolioForm.Folder)]
    [InlineData("Handbook.tar", "zip", FolioForm.Zip)]
    [InlineData("Handbook.zip", "zip", FolioForm.Zip)]
    public void Each_path_gives_its_form(string given, string? named, FolioForm expected)
    {
        FolioVerb.FormOf(given, Path.GetFullPath(given, Path.GetTempPath()), named).ShouldBe(expected);
    }

    [Fact]
    public void A_folder_that_exists_is_the_folder_form_without_a_separator()
    {
        using var workspace = new Workspace();
        Directory.CreateDirectory(workspace.At("Handbook"));

        FolioVerb.FormOf("Handbook", workspace.At("Handbook"), named: null).ShouldBe(FolioForm.Folder);
    }

    [Theory]
    [InlineData("x.html", "--form", "zip")]
    [InlineData("x.zip", "--form", "folder")]
    [InlineData("x/", "--form", "zip")]
    public async Task A_form_that_disagrees_with_the_path_exits_2(string output, string flag, string form)
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", output, flag, form, "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(2, result.ToString());
        result.Error.ShouldContain("but --form says");
        File.Exists(workspace.At(output)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_path_that_names_no_form_exits_2_with_what_to_do()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Handbook", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("Cannot tell the form");
        result.Error.ShouldContain("--form");
    }

    /// <summary>
    /// The page is made by a real browser: the diagram drawn, the picture inside the page, and
    /// unpack gives the documents back, so it is a Folio and not only a page (WP-M2-12).
    /// </summary>
    [Fact]
    public async Task An_html_path_gives_one_page_that_unpacks_to_its_documents()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);
        workspace.Text("docs/flow.md", "# Flow\n\n```mermaid\nflowchart LR\n  A --> B\n```\n");

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Handbook.html", "docs/guide.md", "docs/flow.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());

        string page = await File.ReadAllTextAsync(workspace.At("Handbook.html"), Ct);

        page.ShouldContain("<svg", Case.Sensitive, "the diagram, drawn");
        page.ShouldContain("data:image/png;base64,", Case.Sensitive, "the logo, inside the page");
        Directory.GetFiles(workspace.Root, ".Handbook.*").ShouldBeEmpty("the page is written beside its name and moved onto it");

        CliResult unpacked = await CliRunner.RunAsync(["unpack", "Handbook.html", "--out-dir", "back"], currentDirectory: workspace.Root, cancellationToken: Ct);

        unpacked.ExitCode.ShouldBe(0, unpacked.ToString());
        File.ReadAllText(workspace.At("back/guide.md")).ShouldContain("# Guide");
        File.ReadAllText(workspace.At("back/flow.md")).ShouldContain("flowchart LR");
    }

    /// <summary>CLI-FO-02: past the size the app warns at, the run stops before a browser starts or a byte is written, and says what to do.</summary>
    [Fact]
    public async Task A_single_page_past_the_limit_exits_4_writes_nothing_and_suggests_the_zip()
    {
        using var workspace = new Workspace();
        workspace.Text("docs/big.md", "# Big\n\n![Photo](photo.png)\n");

        // A real PNG's header and then nothing: the planner reads the width from the header and
        // the size from the file, and 40 MB is past 50 once a page carries it as base64.
        using (FileStream photo = File.Create(workspace.At("docs/photo.png")))
        {
            photo.Write(FolioSet.Png);
            photo.SetLength(40L * 1024 * 1024);
        }

        var fake = new FakeRenderers();

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Big.html", "docs/big.md"], currentDirectory: workspace.Root, renderers: fake.Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(4, result.ToString());
        result.Error.ShouldContain("--form zip");
        result.Error.ShouldContain("--shrink-images");
        fake.Made.ShouldBe(0);
        File.Exists(workspace.At("Big.html")).ShouldBeFalse();
    }

    /// <summary>CLI-EX-05 for the page: a document whose render never finishes fails the Folio, and nothing is left behind.</summary>
    [Fact]
    public async Task A_document_that_never_finishes_rendering_fails_the_page_with_exit_5()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);
        workspace.Text("docs/stuck.md", $"# Stuck\n\n{FakeRenderers.NeverFinishes}\n");

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Handbook.html", "docs/guide.md", "docs/stuck.md"], currentDirectory: workspace.Root, renderers: new FakeRenderers().Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(5, result.ToString());
        result.Error.ShouldContain("--timeout");
        File.Exists(workspace.At("Handbook.html")).ShouldBeFalse();
        Directory.GetFiles(workspace.Root, ".Handbook.*").ShouldBeEmpty();
    }

    [Fact]
    public async Task Standard_input_is_refused_because_its_pictures_have_no_folder()
    {
        CliResult result = await CliRunner.RunAsync(["folio", "-o", "x.zip", "-"], standardInput: "# A\n"u8.ToArray(), cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("standard input");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task A_shrink_width_that_is_not_positive_exits_2(string width)
    {
        CliResult result = await CliRunner.RunAsync(["folio", "-o", "x.zip", "--shrink-images", width, "a.md"], cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
    }
}
