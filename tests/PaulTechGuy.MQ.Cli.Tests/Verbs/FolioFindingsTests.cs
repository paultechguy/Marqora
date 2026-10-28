// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>
/// CLI-FO-06: what the app's Folio report lists becomes findings, each on the document it belongs
/// to, in the report's own words. They describe the Folio that was made, so they do not fail the run.
/// </summary>
public sealed class FolioFindingsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_finding_names_its_document_and_line()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set.zip", "docs/guide.md", "docs/setup.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());

        string[] findings = [.. result.Error.Split('\n', StringSplitOptions.TrimEntries).Where(l => l.Contains(" [", StringComparison.Ordinal) && l.EndsWith(']'))];

        findings.ShouldContain("marqora-cli: docs/guide.md:5: warning: Not on this machine: images/gone.png [missing-image]");
        findings.ShouldContain("marqora-cli: docs/guide.md:9: warning: Points outside the Folio: other.md [outside-link]");
        findings.ShouldContain("marqora-cli: docs/setup.md:3: warning: Not on this machine: nothing.png [missing-image]");
        findings.ShouldAllBe(f => f.StartsWith("marqora-cli: docs/guide.md:") || f.StartsWith("marqora-cli: docs/setup.md:"));
    }

    [Fact]
    public async Task Each_document_has_its_own_json_line_with_its_findings()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "--json", "-o", "Set.zip", "docs/guide.md", "docs/setup.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        JsonElement[] lines = FolioSet.JsonLines(result);
        JsonElement setup = lines.Single(l => l.GetProperty("path").GetString() == "docs/setup.md");
        JsonElement set = lines.Single(l => l.GetProperty("path").GetString() == "Set.zip");

        setup.GetProperty("outcome").GetString().ShouldBe("exported");
        setup.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("code").GetString()).ShouldBe(["missing-image"]);
        set.GetProperty("outputs")[0].GetString().ShouldBe(workspace.At("Set.zip"));
    }
}
