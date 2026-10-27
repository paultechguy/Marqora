// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Verbs;

/// <summary>
/// CLI-FO-04: web pictures are collected only under --fetch-web-images, and --dry-run names the
/// sites first. That no socket is opened is proved by OfflineTests (WP-M1-17); these prove what the
/// run says and writes.
/// </summary>
public sealed class FolioFetchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_dry_run_with_fetch_names_each_site_once_and_contacts_none()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);
        workspace.Text("docs/more.md", "![Again](https://pictures.example.com/other.png)\n");

        CliResult text = await CliRunner.RunAsync(["folio", "--dry-run", "--fetch-web-images", "-o", "Set.zip", "docs/guide.md", "docs/more.md"], currentDirectory: workspace.Root, cancellationToken: Ct);
        CliResult json = await CliRunner.RunAsync(["folio", "--dry-run", "--fetch-web-images", "--json", "-o", "Set.zip", "docs/guide.md", "docs/more.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        text.ExitCode.ShouldBe(0, text.ToString());
        text.Out.ShouldContain("Web:        2 pictures to collect from pictures.example.com; none contacted by --dry-run");

        JsonElement preflight = FolioSet.JsonLines(json).Single(l => l.GetProperty("path").GetString() == "Set.zip").GetProperty("preflight");
        preflight.GetProperty("sites").EnumerateArray().Select(s => s.GetString()).ShouldBe(["pictures.example.com"]);
    }

    [Fact]
    public async Task Without_the_flag_web_pictures_stay_links_and_are_reported()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);

        CliResult result = await CliRunner.RunAsync(["folio", "-o", "Set/", "docs/guide.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        workspace.Read("Set/guide.md").ShouldContain("https://pictures.example.com/pic.png");
        result.Error.ShouldContain("docs/guide.md:7: info: Left on the web: https://pictures.example.com/pic.png [remote-image-not-included]");
    }
}
