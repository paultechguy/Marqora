// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>
/// CLI-EX-HTML-01 and CLI-OUT-09: export html writes the app's page, and -o - sends the same bytes
/// to stdout. That the page is the app's own is the comparison with the baseline set (§9.3); this
/// holds the half a test can, through a real browser.
/// </summary>
[Collection(nameof(BrowserRunsAlone))]
public sealed class ExportHtmlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Standard_output_gives_the_same_bytes_as_the_file()
    {
        using var workspace = new Workspace();
        workspace.Text("guide.md", "# Guide\n\nSome *text*, a [link](https://example.com) and code:\n\n```csharp\nvar x = 1;\n```\n\n$$e^{i\\pi} + 1 = 0$$\n");

        CliResult toFile = await CliRunner.RunAsync(["export", "html", "--theme", "forest", "-o", "guide.html", "guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        CliResult toOut = await CliRunner.RunAsync(["export", "html", "--theme", "forest", "-o", "-", "guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        toFile.ExitCode.ShouldBe(0, toFile.ToString());
        toOut.ExitCode.ShouldBe(0, toOut.ToString());

        byte[] file = workspace.ReadBytes("guide.html");

        file.Length.ShouldBeGreaterThan(1000);
        toOut.OutBytes.ShouldBe(file);
        toOut.Error.ShouldContain("exported to standard output.");

        // Nothing is left in the temp folder by the way to stdout.
        Directory.GetFiles(Path.GetTempPath(), ".marqora-cli-stdout.*").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pdf_cannot_go_to_standard_output()
    {
        var fake = new FakeRenderers();

        CliResult result = await CliRunner.RunAsync(["export", "pdf", "-o", "-", "guide.md"], renderers: fake.Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(2, result.ToString());
        result.Error.ShouldContain("-o - cannot be used");
        fake.Made.ShouldBe(0);
    }
}
