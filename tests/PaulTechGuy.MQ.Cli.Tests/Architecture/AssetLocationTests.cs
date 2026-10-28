// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Rendering;
using PaulTechGuy.MQ.Repositories;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Architecture;

/// <summary>
/// CLI-PKG-01: marqora-cli finds the web shell and the themes from where it is installed, never
/// from where it was started. That the release puts it beside the app, sharing one copy of each,
/// is New-Release.ps1's check; these hold the half a test can see.
/// </summary>
public sealed class AssetLocationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_built_cli_carries_a_complete_web_shell_beside_it()
    {
        string installed = Path.GetDirectoryName(Repo.CliExecutable)!;
        var assets = new WebAssetProvider(new AppPaths(Path.Combine(Path.GetTempPath(), "marqora-cli-tests", "no-app"), installed), NullLogger<WebAssetProvider>.Instance);

        assets.IsAvailable.ShouldBeTrue($"missing from {assets.RootDirectory}: {string.Join(", ", assets.MissingAssets)}");
    }

    [Fact]
    public void The_shell_is_looked_for_under_the_executables_own_folder()
    {
        new AppPaths().WebAssetsDirectory.ShouldStartWith(AppContext.BaseDirectory, Case.Insensitive);
    }

    [Fact]
    public async Task Started_from_another_folder_it_still_finds_its_themes()
    {
        using var workspace = new Workspace();

        CliResult result = await CliRunner.RunProcessAsync(["themes", "list"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.Out.ShouldContain("default");
        result.Out.ShouldContain("vintage-press");
    }
}
