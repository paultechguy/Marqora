// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// Writing a file that must never be left half-written, and never leaving the temporary copy
/// behind: in a locked scratch folder on the temp folder's drive, beside the file on any other,
/// and cleared either way when the write is done or the next time it is attempted.
/// </summary>
public sealed class SafeFileWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "marqora-safewrite-tests", Guid.NewGuid().ToString("n"));

    public SafeFileWriterTests()
    {
        Directory.CreateDirectory(Pages);
        Directory.CreateDirectory(Scratch);
    }

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the test is not a failed test.
        }
    }

    private string Pages => Path.Combine(_root, "pages");

    private string Scratch => Path.Combine(_root, "scratch");

    private string Page => Path.Combine(Pages, "notes (review by Marqora).html");

    /// <summary>A scratch root on another drive, so the write goes beside the file as it would on a USB stick.</summary>
    private static string OtherDrive =>
        Path.GetPathRoot(Path.GetTempPath())!.StartsWith('Z') ? @"Y:\scratch" : @"Z:\scratch";

    private Task WriteAsync(string contents, string? scratchRoot = null) =>
        SafeFileWriter.WriteAsync(Page, contents, NullLogger.Instance, scratchRoot ?? Scratch, TestContext.Current.CancellationToken);

    private IEnumerable<string> Leftovers() =>
        Directory.EnumerateFileSystemEntries(Pages).Where(p => p != Page)
            .Concat(Directory.EnumerateFileSystemEntries(Scratch));

    [Fact]
    public async Task A_new_file_is_written_and_nothing_is_left_behind()
    {
        await WriteAsync("<html>one</html>");

        File.ReadAllText(Page).ShouldBe("<html>one</html>");
        File.GetAttributes(Page).HasFlag(FileAttributes.Hidden).ShouldBeFalse();
        Leftovers().ShouldBeEmpty();
    }

    [Fact]
    public async Task An_existing_file_is_replaced_whole()
    {
        await WriteAsync("<html>one</html>");
        await WriteAsync("<html>two</html>");

        File.ReadAllText(Page).ShouldBe("<html>two</html>");
        Leftovers().ShouldBeEmpty();
    }

    /// <summary>The page is the reviewer's save file: a write that fails leaves the one that was there, and no copy.</summary>
    [Fact]
    public async Task A_write_that_fails_leaves_the_original_and_no_copy()
    {
        await WriteAsync("<html>kept</html>");
        File.SetAttributes(Page, FileAttributes.ReadOnly);

        await Should.ThrowAsync<Exception>(() => WriteAsync("<html>lost</html>"));

        File.ReadAllText(Page).ShouldBe("<html>kept</html>");
        Leftovers().ShouldBeEmpty();
    }

    [Fact]
    public async Task On_another_drive_the_copy_goes_beside_the_file_and_is_removed()
    {
        await WriteAsync("<html>beside</html>", OtherDrive);

        File.ReadAllText(Page).ShouldBe("<html>beside</html>");
        File.GetAttributes(Page).HasFlag(FileAttributes.Hidden).ShouldBeFalse();
        Leftovers().ShouldBeEmpty();
    }

    [Fact]
    public async Task On_another_drive_a_failed_write_leaves_no_copy_beside_the_file()
    {
        await WriteAsync("<html>kept</html>", OtherDrive);
        File.SetAttributes(Page, FileAttributes.ReadOnly);

        await Should.ThrowAsync<Exception>(() => WriteAsync("<html>lost</html>", OtherDrive));

        File.ReadAllText(Page).ShouldBe("<html>kept</html>");
        Leftovers().ShouldBeEmpty();
    }

    /// <summary>What a crash part way through a write to another drive leaves, cleared by the next write.</summary>
    [Fact]
    public async Task A_copy_a_crash_left_beside_the_file_is_cleared_by_the_next_write()
    {
        string left = Path.Combine(Pages, $".notes (review by Marqora).html.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(left, "half a page");

        await WriteAsync("<html>whole</html>", OtherDrive);

        File.Exists(left).ShouldBeFalse();
    }

    [Fact]
    public void Only_this_files_unlocked_copies_are_cleared()
    {
        string left = Path.Combine(Pages, $".notes (review by Marqora).html.{Guid.NewGuid():N}.tmp");
        string inUse = Path.Combine(Pages, $".notes (review by Marqora).html.{Guid.NewGuid():N}.tmp");
        string another = Path.Combine(Pages, $".other.html.{Guid.NewGuid():N}.tmp");
        string unlike = Path.Combine(Pages, ".notes (review by Marqora).html.backup.tmp");

        foreach (string file in new[] { left, inUse, another, unlike })
        {
            File.WriteAllText(file, "x");
        }

        using (new FileStream(inUse, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            SafeFileWriter.ClearStaleTemporaries(Page, NullLogger.Instance).ShouldBe(1);
        }

        File.Exists(left).ShouldBeFalse();
        File.Exists(inUse).ShouldBeTrue();
        File.Exists(another).ShouldBeTrue();
        File.Exists(unlike).ShouldBeTrue();
    }

    [Theory]
    [InlineData(".a.html.0123456789abcdef0123456789abcdef.tmp", true)]
    [InlineData(".A.HTML.0123456789abcdef0123456789abcdef.tmp", true)]
    [InlineData(".a.html.0123456789ABCDEF0123456789abcdef.tmp", false)]
    [InlineData(".a.html.tmp", false)]
    [InlineData("a.html.0123456789abcdef0123456789abcdef.tmp", false)]
    [InlineData(".b.html.0123456789abcdef0123456789abcdef.tmp", false)]
    public void A_temporary_copy_is_recognized_by_its_whole_name(string candidate, bool expected)
    {
        SafeFileWriter.IsTemporaryFor(candidate, "a.html").ShouldBe(expected);
    }
}
