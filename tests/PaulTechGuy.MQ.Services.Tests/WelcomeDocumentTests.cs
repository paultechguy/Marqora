// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// When the stock welcome document is put in front of the user, and when it is not.
///
/// The whole feature is one decision - has this version introduced itself yet - and one copy,
/// so the files here are real and only the version and the settings are stood in for.
/// </summary>
public sealed class WelcomeDocumentTests : IDisposable
{
    private const string Shipped = "# Welcome to Marqora\n\nThe shipped copy.\n";

    private readonly TempFolder _folder = new();
    private readonly FakeAppPaths _paths;
    private readonly FakeSettingsService _settings = new();

    public WelcomeDocumentTests()
    {
        _paths = new FakeAppPaths(_folder.Path);
        _paths.EnsureCreated();
    }

    public void Dispose() => _folder.Dispose();

    private void Ship(string text = Shipped) => File.WriteAllText(_paths.WelcomeTemplatePath, text);

    private WelcomeDocumentService ServiceFor(string version, bool requested = false) =>
        new(_paths, _settings, version, requested, NullLogger<WelcomeDocumentService>.Instance);

    private Task<string?> RunAsync(string version) =>
        ServiceFor(version).PrepareAsync(TestContext.Current.CancellationToken);

    /// <summary>A launch that held Shift.</summary>
    private Task<string?> RunRequestedAsync(string version) =>
        ServiceFor(version, requested: true).PrepareAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task The_first_run_of_a_version_copies_the_document_and_hands_back_its_path()
    {
        Ship();

        string? path = await RunAsync("1.0.0");

        path.ShouldBe(_paths.WelcomeDocumentPath);
        File.ReadAllText(path!).ShouldBe(Shipped);
        _settings.Current.LastWelcomeVersion.ShouldBe("1.0.0");
    }

    [Fact]
    public async Task The_second_run_of_the_same_version_offers_nothing()
    {
        Ship();
        await RunAsync("1.0.0");

        // The user's own edits to their copy: they are still there afterwards, because the
        // second run does not touch the file at all.
        File.WriteAllText(_paths.WelcomeDocumentPath, "Mine now.");

        string? path = await RunAsync("1.0.0");

        path.ShouldBeNull();
        File.ReadAllText(_paths.WelcomeDocumentPath).ShouldBe("Mine now.");
    }

    [Fact]
    public async Task A_new_version_refreshes_the_copy_and_offers_it_again()
    {
        Ship();
        await RunAsync("1.0.0");

        File.WriteAllText(_paths.WelcomeDocumentPath, "Last release's copy, scribbled on.");
        Ship("# Welcome to Marqora\n\nWhat 1.1 can do.\n");

        string? path = await RunAsync("1.1.0");

        path.ShouldBe(_paths.WelcomeDocumentPath);
        File.ReadAllText(path!).ShouldContain("What 1.1 can do.");
        _settings.Current.LastWelcomeVersion.ShouldBe("1.1.0");
    }

    [Fact]
    public async Task A_settings_file_from_before_the_welcome_document_existed_counts_as_never_shown()
    {
        Ship();

        AppSettings.Default.LastWelcomeVersion.ShouldBeNull();

        (await RunAsync("1.0.0")).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_read_only_copy_left_by_an_earlier_release_is_still_replaced()
    {
        Ship();
        await RunAsync("1.0.0");

        new FileInfo(_paths.WelcomeDocumentPath).IsReadOnly = true;
        Ship("# Welcome to Marqora\n\nThe next release.\n");

        (await RunAsync("1.1.0")).ShouldNotBeNull();

        File.ReadAllText(_paths.WelcomeDocumentPath).ShouldContain("The next release.");

        // And the copy the user is handed can be saved, which is the point of copying it out
        // of the install folder in the first place.
        new FileInfo(_paths.WelcomeDocumentPath).IsReadOnly.ShouldBeFalse();
    }

    [Fact]
    public async Task A_deployment_missing_the_document_records_nothing_so_it_can_still_appear_later()
    {
        (await RunAsync("1.0.0")).ShouldBeNull();
        _settings.Current.LastWelcomeVersion.ShouldBeNull();

        Ship();

        (await RunAsync("1.0.0")).ShouldBe(_paths.WelcomeDocumentPath);
    }

    // -------------------------------------------------------------- asked for

    [Fact]
    public async Task Holding_Shift_shows_the_document_this_version_has_already_introduced()
    {
        Ship();
        await RunAsync("1.0.0");

        (await RunRequestedAsync("1.0.0")).ShouldBe(_paths.WelcomeDocumentPath);
    }

    [Fact]
    public async Task Holding_Shift_puts_back_a_copy_that_had_been_written_over()
    {
        Ship();
        await RunAsync("1.0.0");

        File.WriteAllText(_paths.WelcomeDocumentPath, "Nothing like the original.");

        (await RunRequestedAsync("1.0.0")).ShouldNotBeNull();
        File.ReadAllText(_paths.WelcomeDocumentPath).ShouldBe(Shipped);
    }

    [Fact]
    public async Task A_launch_that_asked_for_it_says_so_and_still_needs_something_to_copy()
    {
        ServiceFor("1.0.0", requested: true).WasRequested.ShouldBeTrue();
        ServiceFor("1.0.0").WasRequested.ShouldBeFalse();

        // Nothing shipped, so there is nothing to open however loudly it was asked for.
        (await RunRequestedAsync("1.0.0")).ShouldBeNull();
    }
}
