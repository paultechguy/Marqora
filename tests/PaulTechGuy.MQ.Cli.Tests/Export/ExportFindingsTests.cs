// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>CLI-EX-03: what the export report would list becomes findings on the document's own lines, and the export still succeeds.</summary>
public sealed class ExportFindingsTests
{
    [Fact]
    public async Task A_missing_picture_exports_exits_0_and_is_reported_with_its_line()
    {
        using var workspace = new Workspace();
        workspace.Text("guide.md", "# Guide\n\n![The chart](chart.png)\n");

        CliResult result = await CliRunner.RunAsync(
            ["export", "pdf", "--json", "-o", "guide.pdf", "guide.md"],
            currentDirectory: workspace.Root,
            renderers: new FakeRenderers().Make,
            cancellationToken: TestContext.Current.CancellationToken);

        result.ExitCode.ShouldBe(0, result.ToString());
        File.Exists(workspace.At("guide.pdf")).ShouldBeTrue();
        result.Error.ShouldContain("guide.md:3: warning: Not found: chart.png [not-found]");

        using JsonDocument line = JsonDocument.Parse(result.Out);
        line.RootElement.GetProperty("outcome").GetString().ShouldBe("exported");

        JsonElement finding = line.RootElement.GetProperty("findings")[0];
        finding.GetProperty("line").GetInt32().ShouldBe(3);
        finding.GetProperty("code").GetString().ShouldBe("not-found");
    }
}
