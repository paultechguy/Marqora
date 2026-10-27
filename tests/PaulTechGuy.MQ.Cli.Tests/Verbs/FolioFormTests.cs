// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Cli.Verbs;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>CLI-FO-01: the form comes from the output path, --form decides a path that says nothing, and the two never disagree quietly.</summary>
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

    /// <summary>The single page needs the headless browser, which comes with M2 (WP-M2-12).</summary>
    [Fact]
    public async Task The_single_page_exits_2_until_a_later_release()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Handbook.html", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("later release");
        File.Exists(workspace.At("Handbook.html")).ShouldBeFalse();
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
