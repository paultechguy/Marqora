// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// A share's temporary folder and the sweep that clears what a stopped process left: a folder a
/// running share holds is never taken, one nothing holds always is, and the sweep cannot be
/// pointed anywhere but its own root.
/// </summary>
public sealed class FolioScratchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "marqora-scratch-tests", Guid.NewGuid().ToString("n"));

    public FolioScratchTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the test is not a failed test.
        }
    }

    private static int Sweep(string root, DateTime? now = null) =>
        FolioScratch.SweepStale(NullLogger.Instance, root, now);

    /// <summary>A folder as a stopped process leaves it: its lock closed, its contents still there.</summary>
    private string Abandoned()
    {
        string folder;

        using (FolioScratchFolder scratch = FolioScratch.Create(NullLogger.Instance, _root))
        {
            folder = scratch.Path;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ".lock"), string.Empty);
        File.WriteAllText(Path.Combine(folder, "picture.png"), "bytes");

        return folder;
    }

    [Fact]
    public void A_share_removes_its_own_folder_when_it_ends()
    {
        string path;

        using (FolioScratchFolder scratch = FolioScratch.Create(NullLogger.Instance, _root))
        {
            path = scratch.Path;
            File.WriteAllText(Path.Combine(path, "stand-in.png"), "bytes");
        }

        Directory.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void A_folder_a_running_share_holds_is_left_alone()
    {
        using FolioScratchFolder scratch = FolioScratch.Create(NullLogger.Instance, _root);

        Sweep(_root, DateTime.UtcNow.AddDays(3)).ShouldBe(0);

        Directory.Exists(scratch.Path).ShouldBeTrue();
    }

    [Fact]
    public void A_folder_a_stopped_process_left_is_swept_at_once()
    {
        string left = Abandoned();

        Sweep(_root).ShouldBe(1);

        Directory.Exists(left).ShouldBeFalse();
    }

    [Fact]
    public void A_folder_not_named_as_a_share_names_one_is_never_touched()
    {
        string other = Path.Combine(_root, "someone-elses");
        Directory.CreateDirectory(other);

        Sweep(_root, DateTime.UtcNow.AddDays(3)).ShouldBe(0);

        Directory.Exists(other).ShouldBeTrue();
    }

    /// <summary>A folder from before the lock existed may belong to a share running in an older window.</summary>
    [Fact]
    public void A_lockless_folder_is_swept_only_once_it_is_old()
    {
        string lockless = Path.Combine(_root, Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(lockless);

        Sweep(_root).ShouldBe(0);
        Directory.Exists(lockless).ShouldBeTrue();

        Sweep(_root, DateTime.UtcNow + FolioScratch.LocklessAge + TimeSpan.FromMinutes(1)).ShouldBe(1);
        Directory.Exists(lockless).ShouldBeFalse();
    }

    /// <summary>A junction planted where a share's folder would be is not followed, so what it points at survives.</summary>
    [Fact]
    public void A_junction_is_never_followed()
    {
        string target = Path.Combine(_root, "precious");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "mine");

        string junction = Path.Combine(_root, Guid.NewGuid().ToString("n"));

        if (!TryMakeJunction(junction, target))
        {
            return;
        }

        Sweep(_root, DateTime.UtcNow.AddDays(3));

        File.Exists(Path.Combine(target, "keep.txt")).ShouldBeTrue();
    }

    [Fact]
    public void A_missing_root_sweeps_nothing()
    {
        Sweep(Path.Combine(_root, "not-there")).ShouldBe(0);
    }

    /// <summary>mklink /J needs no elevation; where even that is unavailable, there is nothing to test.</summary>
    private static bool TryMakeJunction(string link, string target)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            process?.WaitForExit(10_000);

            return Directory.Exists(link);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
