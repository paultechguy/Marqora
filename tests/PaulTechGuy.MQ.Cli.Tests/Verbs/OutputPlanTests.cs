// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Inputs;
using PaulTechGuy.MQ.Cli.Outputs;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>
/// CLI-OUT-07 and CLI-OUT-08: every output is checked before any work starts. Driven through folio
/// and export pdf, and - for the collision - through OutputPlan directly as well.
/// </summary>
public sealed class OutputPlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_output_that_is_an_input_is_refused_even_with_overwrite()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "docs/guide.md", "--form", "zip", "--overwrite", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(4, result.ToString());
        result.Error.ShouldContain("one of the inputs");
        workspace.Read("docs/guide.md").ShouldBe(FolioSet.Guide.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task An_existing_output_exits_4_before_planning_and_is_left_as_it_was()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);
        workspace.Text("Set.zip", "not a zip");

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set.zip", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(4);
        result.Error.ShouldContain("Pass --overwrite to replace it");
        workspace.Read("Set.zip").ShouldBe("not a zip");

        // Nothing was planned: the report's findings come from the plan, and there are none.
        result.Error.ShouldNotContain("[missing-image]");
    }

    [Fact]
    public async Task With_overwrite_an_existing_zip_is_replaced()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);
        workspace.Text("Set.zip", "not a zip");

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set.zip", "--overwrite", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        workspace.ReadBytes("Set.zip")[..2].ShouldBe("PK"u8.ToArray());
    }

    [Fact]
    public async Task A_folder_with_anything_in_it_is_refused_without_overwrite()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);
        workspace.Text("Set/mine.md", "my own file\n");

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set/", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(4);
        result.Error.ShouldContain("Pass --overwrite to replace its contents");
        Directory.GetFileSystemEntries(workspace.At("Set")).Select(Path.GetFileName).ShouldBe(["mine.md"]);
    }

    /// <summary>A5c: --overwrite replaces what is in the folder, and nothing is left beside it.</summary>
    [Fact]
    public async Task With_overwrite_a_folders_contents_are_replaced()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);
        workspace.Text("Set/mine.md", "my own file\n");

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set/", "--overwrite", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        File.Exists(workspace.At("Set/mine.md")).ShouldBeFalse();
        File.Exists(workspace.At("Set/guide.md")).ShouldBeTrue();
        Directory.GetFileSystemEntries(workspace.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["Set", "docs"]);
    }

    [Fact]
    public async Task A_folder_holding_an_input_is_never_emptied()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "docs/", "--overwrite", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(4);
        result.Error.ShouldContain("which this run reads");
        workspace.Read("docs/setup.md").ShouldBe(FolioSet.Setup.ReplaceLineEndings("\n"));
    }

    /// <summary>The pictures are not inputs, but the Folio reads them: their folder is not emptied either.</summary>
    [Fact]
    public async Task A_folder_holding_a_picture_the_folio_collects_is_never_emptied()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "docs/images/", "--overwrite", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(4);
        result.Error.ShouldContain("logo.png");
        File.Exists(workspace.At("docs/images/logo.png")).ShouldBeTrue();
    }

    [Fact]
    public async Task The_current_folder_and_any_folder_holding_it_are_never_emptied()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);
        workspace.Text("work/notes.md", "mine\n");

        CliResult here = await CliRunner.RunAsync(["folio", "-o", "./", "--overwrite", "../docs/guide.md"], currentDirectory: workspace.At("work"), cancellationToken: Ct);
        CliResult above = await CliRunner.RunAsync(["folio", "-o", "../", "--overwrite", "../docs/guide.md"], currentDirectory: workspace.At("work"), cancellationToken: Ct);

        here.ExitCode.ShouldBe(4);
        here.Error.ShouldContain("current folder");
        above.ExitCode.ShouldBe(4);
        File.Exists(workspace.At("work/notes.md")).ShouldBeTrue();
    }

    [Fact]
    public async Task An_empty_folder_is_filled()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);
        Directory.CreateDirectory(workspace.At("Set"));

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        File.Exists(workspace.At("Set/guide.md")).ShouldBeTrue();
    }

    [Fact]
    public void Two_inputs_colliding_in_one_out_dir_exit_3()
    {
        var error = new StringWriter();
        RunContext context = Context(error);

        InputFile a = new("docs/a.md", Path.GetFullPath("docs/a.md"));
        InputFile b = new("notes/a.md", Path.GetFullPath("notes/a.md"));
        string dist = Path.Combine(Path.GetTempPath(), "marqora-cli-tests", "dist", "a.pdf");

        ExitCode code = OutputPlan.Check(context, [a, b], [new PlannedOutput(a.Display, dist, OutputKind.File), new PlannedOutput(b.Display, dist, OutputKind.File)], overwrite: false);

        code.ShouldBe(ExitCode.Input);
        error.ToString().ShouldContain("docs/a.md and notes/a.md would both be written to");
    }

    [Fact]
    public async Task Out_dir_names_each_export_after_its_document()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "# A\n");
        workspace.Text("b.md", "# B\n");

        CliResult result = await CliRunner.RunAsync(["export", "pdf", "--out-dir", "dist", "a.md", "b.md"], currentDirectory: workspace.Root, renderers: new FakeRenderers().Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        File.Exists(workspace.At("dist/a.pdf")).ShouldBeTrue();
        File.Exists(workspace.At("dist/b.pdf")).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_o_or_out_dir_each_export_goes_beside_its_document()
    {
        using var workspace = new Workspace();
        workspace.Text("docs/a.md", "# A\n");
        workspace.Text("notes/b.md", "# B\n");

        CliResult result = await CliRunner.RunAsync(["export", "pdf", "docs/a.md", "notes/b.md"], currentDirectory: workspace.Root, renderers: new FakeRenderers().Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        File.Exists(workspace.At("docs/a.pdf")).ShouldBeTrue();
        File.Exists(workspace.At("notes/b.pdf")).ShouldBeTrue();
    }

    [Fact]
    public async Task Standard_input_without_o_exits_2()
    {
        var fake = new FakeRenderers();

        CliResult result = await CliRunner.RunAsync(["export", "pdf", "-"], standardInput: "# A\n"u8.ToArray(), renderers: fake.Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(2, result.ToString());
        result.Error.ShouldContain("name the file with -o");
        fake.Made.ShouldBe(0);
    }

    [Fact]
    public async Task An_export_with_o_and_two_inputs_exits_2()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "# A\n");
        workspace.Text("b.md", "# B\n");
        var fake = new FakeRenderers();

        CliResult result = await CliRunner.RunAsync(["export", "pdf", "-o", "x.pdf", "a.md", "b.md"], currentDirectory: workspace.Root, renderers: fake.Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(2, result.ToString());
        fake.Made.ShouldBe(0);
    }

    [Fact]
    public async Task An_existing_export_exits_4_without_starting_a_browser()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "# A\n");
        workspace.Text("a.pdf", "an older PDF");
        var fake = new FakeRenderers();

        CliResult result = await CliRunner.RunAsync(["export", "pdf", "-o", "a.pdf", "a.md"], currentDirectory: workspace.Root, renderers: fake.Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(4, result.ToString());
        result.Error.ShouldContain("already exists. Pass --overwrite to replace it.");
        fake.Made.ShouldBe(0);
        workspace.Read("a.pdf").ShouldBe("an older PDF");
    }

    [Fact]
    public async Task Two_documents_of_one_name_into_one_out_dir_exit_3_before_anything_is_written()
    {
        using var workspace = new Workspace();
        workspace.Text("docs/a.md", "# A\n");
        workspace.Text("notes/a.md", "# Another A\n");
        var fake = new FakeRenderers();

        CliResult result = await CliRunner.RunAsync(["export", "pdf", "--out-dir", "dist", "docs/a.md", "notes/a.md"], currentDirectory: workspace.Root, renderers: fake.Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3, result.ToString());
        fake.Made.ShouldBe(0);
        Directory.Exists(workspace.At("dist")).ShouldBeFalse();
    }

    private static RunContext Context(StringWriter error)
    {
        var environment = new CliEnvironment(TextWriter.Null, error, () => Stream.Null, false, _ => null, Path.GetTempPath());

        return new RunContext
        {
            Environment = environment,
            Reporter = new Reporter(error, Verbosity.Normal, color: false),
            Results = new ResultWriter(TextWriter.Null, json: false),
            Verb = "export pdf",
            FailFast = false,
            Settings = null,
            CancellationToken = Ct,
        };
    }
}
