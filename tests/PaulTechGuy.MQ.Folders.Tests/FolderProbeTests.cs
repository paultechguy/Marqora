// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folders.Tests;

public sealed class FolderProbeTests
{
    [Fact]
    public void A_folder_with_markdown_deep_below_leads()
    {
        using var temp = new TempFolder();
        temp.File("src/a/b/c/notes.md");

        FolderProbe.Look(temp.Full("src"), 1000, CancellationToken.None).ShouldBe(ProbeResult.Leads);
    }

    [Fact]
    public void An_image_is_enough()
    {
        using var temp = new TempFolder();
        temp.File("art/logo.png");

        FolderProbe.Look(temp.Full("art"), 1000, CancellationToken.None).ShouldBe(ProbeResult.Leads);
    }

    [Fact]
    public void A_folder_of_code_is_empty()
    {
        using var temp = new TempFolder();
        temp.File("src/Program.cs");
        temp.File("src/sub/Helper.cs");

        FolderProbe.Look(temp.Full("src"), 1000, CancellationToken.None).ShouldBe(ProbeResult.Empty);
    }

    [Fact]
    public void Markdown_inside_hidden_folders_and_dot_files_does_not_count()
    {
        using var temp = new TempFolder();
        temp.File("lib/node_modules/pkg/README.md");
        temp.File("lib/.cache/notes.md");
        temp.File("lib/.hidden.md");

        FolderProbe.Look(temp.Full("lib"), 1000, CancellationToken.None).ShouldBe(ProbeResult.Empty);
    }

    [Fact]
    public void The_ceiling_stops_the_look_and_the_answer_is_unknown()
    {
        using var temp = new TempFolder();

        for (int i = 0; i < 30; i++)
        {
            temp.File($"big/f{i:00}.cs");
        }

        temp.File("big/zz/notes.md");

        FolderProbe.Look(temp.Full("big"), 10, CancellationToken.None).ShouldBe(ProbeResult.Unknown);
    }

    [Fact]
    public void A_canceled_look_throws_rather_than_answering()
    {
        using var temp = new TempFolder();
        temp.File("src/a.cs");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Should.Throw<OperationCanceledException>(() => FolderProbe.Look(temp.Full("src"), 1000, cancel.Token));
    }

    [Fact]
    public async Task Probes_canceled_when_the_root_changes_end_without_an_answer()
    {
        using var temp = new TempFolder();
        temp.File("a/x.cs");
        temp.File("b/y.cs");
        temp.File("c/z.cs");
        using var probe = new FolderProbe();
        using var rootChanged = new CancellationTokenSource();

        rootChanged.Cancel();

        Task<ProbeResult>[] looks =
        [
            probe.ProbeAsync(temp.Full("a"), rootChanged.Token),
            probe.ProbeAsync(temp.Full("b"), rootChanged.Token),
            probe.ProbeAsync(temp.Full("c"), rootChanged.Token),
        ];

        foreach (Task<ProbeResult> look in looks)
        {
            await Should.ThrowAsync<OperationCanceledException>(look);
        }

        // The slots were all given back: a fresh look still runs.
        (await probe.ProbeAsync(temp.Full("a"), TestContext.Current.CancellationToken)).ShouldBe(ProbeResult.Empty);
    }

    [Fact]
    public void A_missing_folder_is_unknown_so_it_is_not_hidden_by_mistake()
    {
        using var temp = new TempFolder();

        FolderProbe.Look(temp.Full("gone"), 1000, CancellationToken.None).ShouldBe(ProbeResult.Unknown);
    }
}
