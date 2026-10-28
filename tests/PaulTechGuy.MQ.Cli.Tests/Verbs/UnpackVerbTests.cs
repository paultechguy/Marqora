// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Folio;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>
/// CLI-VB-UNP-01 with amendments A4 and A5b: a single-file Folio unpacks; folder and zip Folios
/// and a non-empty target are refused. The Folio is built the way the app's writer builds one,
/// with the Folio library's own marker and payload; unpacking a Folio the app itself made is
/// recorded in the plan's §9.3, from the M0-05 baseline.
/// </summary>
public sealed class UnpackVerbTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Folio(params (string Entry, string Text)[] documents) =>
        $"""
         <!DOCTYPE html>
         <html><head>{FolioMarkup.MarkerMeta}</head>
         <body><p>A Folio.</p>
         {FolioPayload.Encode(new FolioPayload { Documents = [.. documents.Select(d => new FolioPayloadDocument { Entry = d.Entry, Text = d.Text })] })}
         </body></html>
         """;

    [Fact]
    public async Task A_single_file_folio_unpacks_its_documents()
    {
        using var workspace = new Workspace();
        workspace.Text("Handbook.html", Folio(("guide.md", "# Guide\n"), ("parts/setup.md", "# Setup\n")));

        CliResult result = await CliRunner.RunAsync(["unpack", "Handbook.html", "--out-dir", "Handbook"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        workspace.Read("Handbook/guide.md").ShouldBe("# Guide\n");
        workspace.Read("Handbook/parts/setup.md").ShouldBe("# Setup\n");
        result.Error.ShouldContain("unpacked 2 documents");
    }

    [Fact]
    public async Task A_non_empty_target_exits_4_and_is_left_as_it_was()
    {
        using var workspace = new Workspace();
        workspace.Text("Handbook.html", Folio(("guide.md", "# Guide\n")));
        workspace.Text("Handbook/mine.md", "my own file\n");

        CliResult result = await CliRunner.RunAsync(["unpack", "Handbook.html", "--out-dir", "Handbook"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(4);
        result.Error.ShouldContain("Pass --overwrite to replace its contents");
        Directory.GetFiles(workspace.At("Handbook")).Select(Path.GetFileName).ShouldBe(["mine.md"]);
    }

    /// <summary>A5c: --overwrite replaces what is in the target, and nothing is left beside it.</summary>
    [Fact]
    public async Task With_overwrite_a_non_empty_target_is_replaced()
    {
        using var workspace = new Workspace();
        workspace.Text("Handbook.html", Folio(("guide.md", "# Guide\n")));
        workspace.Text("Handbook/mine.md", "my own file\n");

        CliResult result = await CliRunner.RunAsync(["unpack", "Handbook.html", "--out-dir", "Handbook", "--overwrite", "--json"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        Directory.GetFiles(workspace.At("Handbook")).Select(Path.GetFileName).ShouldBe(["guide.md"]);
        Directory.GetFileSystemEntries(workspace.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ShouldBe(["Handbook", "Handbook.html"]);

        // The documents are named where they ended up, not where they were unpacked first.
        result.Out.ShouldContain(workspace.At("Handbook").Replace("\\", "\\\\", StringComparison.Ordinal));
        result.Out.ShouldNotContain(".tmp");
    }

    [Fact]
    public async Task A_target_holding_the_folio_is_never_emptied()
    {
        using var workspace = new Workspace();
        workspace.Text("Shared/Handbook.html", Folio(("guide.md", "# Guide\n")));

        CliResult result = await CliRunner.RunAsync(["unpack", "Shared/Handbook.html", "--out-dir", "Shared", "--overwrite"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(4);
        result.Error.ShouldContain("which this run reads");
        File.Exists(workspace.At("Shared/Handbook.html")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_folder_folio_is_refused_with_what_to_do()
    {
        using var workspace = new Workspace();
        workspace.Text("Handbook/guide.md", "# Guide\n");

        CliResult result = await CliRunner.RunAsync(["unpack", "Handbook", "--out-dir", "Out"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        result.Error.ShouldContain("already its documents and pictures");
    }

    [Fact]
    public async Task A_zip_folio_is_refused_with_what_to_do()
    {
        using var workspace = new Workspace();
        workspace.Bytes("Handbook.zip", [0x50, 0x4B, 0x03, 0x04]);

        CliResult result = await CliRunner.RunAsync(["unpack", "Handbook.zip", "--out-dir", "Out"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        result.Error.ShouldContain("extract it with any zip tool");
    }

    [Fact]
    public async Task A_page_that_is_not_a_folio_is_refused()
    {
        using var workspace = new Workspace();
        workspace.Text("page.html", "<html><body>Just a page.</body></html>");

        CliResult result = await CliRunner.RunAsync(["unpack", "page.html", "--out-dir", "Out"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        Directory.Exists(workspace.At("Out")).ShouldBeFalse();
    }

    /// <summary>A Folio comes from someone else: an entry that climbs out of the target is refused, not written.</summary>
    [Fact]
    public async Task An_entry_that_leaves_the_folder_is_refused()
    {
        using var workspace = new Workspace();
        workspace.Text("Handbook.html", Folio(("guide.md", "# Guide\n"), ("../escape.md", "# Out\n")));

        CliResult result = await CliRunner.RunAsync(["unpack", "Handbook.html", "--out-dir", "Handbook"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(3);
        File.Exists(workspace.At("escape.md")).ShouldBeFalse();
        workspace.Read("Handbook/guide.md").ShouldBe("# Guide\n");
    }
}
