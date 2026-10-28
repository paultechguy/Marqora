// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Repositories;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Settings;

/// <summary>
/// CLI-SET-01 to CLI-SET-04: hermetic by default, the app's saved Preferences on request, an
/// exported file on request, and a flag on the command line beating the same setting from either.
/// The app's settings are written by its own repository into a folder of the test's own, so no test
/// reads the Preferences of the machine it runs on.
/// </summary>
public sealed class SettingsSourceTests
{
    /// <summary>Trailing spaces, which the default rules strip and a rule switched off keeps.</summary>
    private const string Document = "# Title\n\nText   \n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AppSettings KeepingTrailingSpaces() =>
        new() { FormatRules = FormatOptions.Default with { TrailingWhitespace = false } };

    private static async Task<AppPaths> AppWithAsync(Workspace workspace, AppSettings settings)
    {
        var paths = new AppPaths(workspace.At("appdata"), Repo.Root);
        paths.EnsureCreated();

        using var repository = new JsonSettingsRepository(paths, NullLogger<JsonSettingsRepository>.Instance);
        await repository.SaveAsync(settings, Ct);

        return paths;
    }

    [Fact]
    public async Task Without_settings_the_apps_preferences_make_no_difference()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", Document);
        AppPaths paths = await AppWithAsync(workspace, KeepingTrailingSpaces());

        CliResult withApp = await CliRunner.RunAsync(["format", "a.md"], currentDirectory: workspace.Root, appPaths: paths, cancellationToken: Ct);
        CliResult withoutApp = await CliRunner.RunAsync(["format", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        withApp.OutBytes.ShouldBe(withoutApp.OutBytes);
        withApp.Out.ShouldBe("# Title\n\nText\n");
    }

    [Fact]
    public async Task Settings_app_follows_a_changed_formatter_option_and_never_writes_the_file()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", Document);
        AppPaths paths = await AppWithAsync(workspace, KeepingTrailingSpaces());
        byte[] before = File.ReadAllBytes(paths.SettingsFilePath);

        CliResult result = await CliRunner.RunAsync(["format", "--settings", "app", "a.md"], currentDirectory: workspace.Root, appPaths: paths, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.Out.ShouldBe(Document);
        File.ReadAllBytes(paths.SettingsFilePath).ShouldBe(before);
    }

    [Fact]
    public async Task An_exported_preferences_file_gives_the_same_output_as_settings_app()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", Document);
        AppPaths paths = await AppWithAsync(workspace, KeepingTrailingSpaces());

        var transfer = new PreferencesTransferService("1.0.0", NullLogger<PreferencesTransferService>.Instance);
        await transfer.ExportAsync(workspace.At("exported.json"), KeepingTrailingSpaces(), Ct);

        CliResult fromApp = await CliRunner.RunAsync(["format", "--settings", "app", "a.md"], currentDirectory: workspace.Root, appPaths: paths, cancellationToken: Ct);
        CliResult fromFile = await CliRunner.RunAsync(["format", "--settings", "exported.json", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        fromFile.ExitCode.ShouldBe(0, fromFile.ToString());
        fromFile.OutBytes.ShouldBe(fromApp.OutBytes);
    }

    /// <summary>Amendment A8: a settings.json copied from another machine is accepted, as Import accepts one.</summary>
    [Fact]
    public async Task A_raw_settings_json_is_accepted_as_a_settings_file()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", Document);
        AppPaths paths = await AppWithAsync(workspace, KeepingTrailingSpaces());
        File.Copy(paths.SettingsFilePath, workspace.At("copied-settings.json"));

        CliResult result = await CliRunner.RunAsync(["format", "--settings", "copied-settings.json", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.Out.ShouldBe(Document);
    }

    [Fact]
    public async Task A_malformed_settings_file_exits_2_naming_it()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", Document);
        workspace.Text("bad.json", "{ this is not json");

        CliResult result = await CliRunner.RunAsync(["format", "--settings", "bad.json", "a.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("bad.json");
        result.Out.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_settings_file_exits_2()
    {
        CliResult result = await CliRunner.RunAsync(["format", "--settings", "nowhere.json", "a.md"], cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("nowhere.json");
    }

    [Fact]
    public async Task Settings_app_with_no_saved_preferences_exits_2_rather_than_falling_back()
    {
        CliResult result = await CliRunner.RunAsync(["format", "--settings", "app", "a.md"], cancellationToken: Ct);

        result.ExitCode.ShouldBe(2);
        result.Error.ShouldContain("found no saved Preferences");
    }

    /// <summary>CLI-SET-04: a flag beats the same setting from the settings in force.</summary>
    [Fact]
    public async Task A_flag_beats_the_settings()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "# A\n");
        AppPaths paths = await AppWithAsync(workspace, new AppSettings { HeadingNumberStyle = HeadingNumberStyle.DotSpace });

        CliResult fromSettings = await CliRunner.RunAsync(["number", "--settings", "app", "a.md"], currentDirectory: workspace.Root, appPaths: paths, cancellationToken: Ct);
        CliResult fromFlag = await CliRunner.RunAsync(["number", "--settings", "app", "--style", "two-spaces", "a.md"], currentDirectory: workspace.Root, appPaths: paths, cancellationToken: Ct);

        fromSettings.Out.ShouldBe("# 1. A\n");
        fromFlag.Out.ShouldBe("# 1  A\n");
    }
}
