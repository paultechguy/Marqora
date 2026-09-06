// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

public sealed class PastedImageTrackerTests : IDisposable
{
    private const string Reference = "guide.assets/image.png";

    private readonly TempFolder _folder = new();
    private readonly FakeAppPaths _paths;
    private readonly PastedImageTracker _tracker;
    private readonly Guid _document = Guid.NewGuid();
    private readonly string _imagePath;

    public PastedImageTrackerTests()
    {
        _paths = new FakeAppPaths(Path.Combine(_folder.Path, "appdata"));
        _tracker = new PastedImageTracker(_paths, NullLogger<PastedImageTracker>.Instance);

        _imagePath = Path.Combine(_folder.Path, "guide.assets", "image.png");

        Directory.CreateDirectory(Path.GetDirectoryName(_imagePath)!);
        File.WriteAllBytes(_imagePath, [1, 2, 3]);

        _tracker.Track(_document, Reference, _imagePath);
    }

    private PastedImageReview Review(string text, string savedText = "", params string[] others) =>
        _tracker.ReviewAsync(_document, text, savedText, others).GetAwaiter().GetResult();

    private bool ImageExists => File.Exists(_imagePath);

    [Fact]
    public void An_image_the_document_still_references_is_left_alone()
    {
        Review($"# Guide\n\n![]({Reference})");

        ImageExists.ShouldBeTrue();
    }

    [Fact]
    public void Undoing_the_paste_takes_the_file_out_of_the_folder()
    {
        Review("# Guide\n");

        ImageExists.ShouldBeFalse();
    }

    [Fact]
    public void A_file_copied_in_from_explorer_and_then_undone_is_taken_away_too()
    {
        // The exact reported case: an images folder that already held image.png, a copy of it
        // pasted in as image-1.png, then Ctrl+Z. Nothing about arriving as a file rather than a
        // bitmap changes what an undo means.
        string sibling = Path.Combine(_folder.Path, "images", "image.png");
        string pasted = Path.Combine(_folder.Path, "images", "image-1.png");

        Directory.CreateDirectory(Path.GetDirectoryName(pasted)!);
        File.WriteAllBytes(sibling, [4, 5]);
        File.WriteAllBytes(pasted, [6, 7]);

        var document = Guid.NewGuid();
        _tracker.Track(document, "images/image-1.png", pasted);

        PastedImageReview review = _tracker
            .ReviewAsync(document, "# Guide\n", "", []).GetAwaiter().GetResult();

        review.Recycled.ShouldBe(1);
        review.Failed.ShouldBe(0);

        File.Exists(pasted).ShouldBeFalse();

        // The file that was already there is untouched. Only what this session wrote is in play.
        File.Exists(sibling).ShouldBeTrue();
    }

    [Fact]
    public async Task A_file_something_else_is_briefly_holding_is_still_taken_away()
    {
        // The reported failure. A picture written seconds ago is routinely untouchable for a
        // moment - Explorer building a thumbnail for it, a scanner reading a new file in
        // Downloads - and one attempt lost that race. The symptom was awful to chase: the undo
        // removed the reference, the file stayed, and the very same move worked perfectly by the
        // time anyone looked.
        using (var hold = new FileStream(_imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // Released while the first retry is still waiting.
            _ = Task.Run(async () =>
            {
                await Task.Delay(60, TestContext.Current.CancellationToken);
                await hold.DisposeAsync();
            });

            PastedImageReview review = await _tracker
                .ReviewAsync(_document, "# Guide\n", "", []);

            review.Recycled.ShouldBe(1);
            review.Failed.ShouldBe(0);
        }

        ImageExists.ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_held_open_throughout_is_reported_rather_than_lost_track_of()
    {
        // The other half: when the retries really do run out, the caller has to hear about it.
        // The reference is already gone from the document, so a file left here has nothing
        // pointing at it and no way for anyone to notice on their own.
        using var hold = new FileStream(_imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        PastedImageReview review = await _tracker.ReviewAsync(_document, "# Guide\n", "", []);

        review.Failed.ShouldBe(1);
        review.Recycled.ShouldBe(0);
        ImageExists.ShouldBeTrue();
    }

    [Fact]
    public void The_review_reports_what_it_did_so_a_failure_is_not_silent()
    {
        // The counts exist because a file that should have gone and did not looks exactly like
        // a file that was never tracked. Without them the feature cannot be told from its own
        // absence, which is precisely how a silent failure went unnoticed.
        Review($"![]({Reference})").Recycled.ShouldBe(0);

        Review("# Guide\n").Recycled.ShouldBe(1);
        Review($"![]({Reference})").Restored.ShouldBe(1);
    }

    [Fact]
    public async Task Undoing_a_second_paste_of_the_same_name_works_and_keeps_the_first_copy()
    {
        // Paste, undo, paste again, undo again. The second paste reuses the name because the
        // first file is gone, so both recycles aim at the same folder - and the destination used
        // to be derived from the path, so the second one had to overwrite the first. It did not
        // even manage that: it was refused, and the file stayed in the document's folder with
        // nothing referring to it.
        Review("# Guide\n").Recycled.ShouldBe(1);

        // The same path pasted again, as a fresh capture would be.
        File.WriteAllBytes(_imagePath, [9, 9, 9]);
        _tracker.Track(_document, Reference, _imagePath);

        PastedImageReview second = await _tracker.ReviewAsync(_document, "# Guide\n", "", []);

        second.Recycled.ShouldBe(1);
        second.Failed.ShouldBe(0);
        ImageExists.ShouldBeFalse();

        // Two files in the recycle folder, not one overwritten by the other. Everything in there
        // is something somebody undid and might still want.
        Directory.EnumerateFiles(_paths.RecycleDirectory).Count().ShouldBe(2);
    }

    [Fact]
    public void The_file_is_moved_rather_than_destroyed()
    {
        Review("# Guide\n");

        // The safety rule that makes the whole feature acceptable: a mistake here costs a folder
        // to look in, not somebody's screenshot.
        Directory.EnumerateFiles(_paths.RecycleDirectory).ShouldNotBeEmpty();
    }

    [Fact]
    public void Redo_brings_it_back()
    {
        Review("# Guide\n");
        ImageExists.ShouldBeFalse();

        Review($"# Guide\n\n![]({Reference})");

        ImageExists.ShouldBeTrue();
        File.ReadAllBytes(_imagePath).ShouldBe([1, 2, 3]);
    }

    // ------------------------------------------------- the rules that stop it going wrong

    [Fact]
    public void Once_saved_with_the_reference_the_file_is_never_touched_again()
    {
        string saved = $"# Guide\n\n![]({Reference})";

        // Saving commits it. Deleting the line afterwards is ordinary editing of a real
        // document, not an undone paste.
        Review(saved, savedText: saved);
        Review("# Guide\n", savedText: saved);

        ImageExists.ShouldBeTrue();
    }

    [Fact]
    public void A_saved_document_that_is_then_closed_keeps_its_images()
    {
        string saved = $"![]({Reference})";

        Review(saved, savedText: saved);
        _tracker.Forget(_document);

        ImageExists.ShouldBeTrue();
    }

    [Fact]
    public void Closing_a_tab_restores_anything_currently_recycled()
    {
        Review("# Guide\n");
        ImageExists.ShouldBeFalse();

        // A tab closed while an image sat recycled must not be how a file disappears.
        _tracker.Forget(_document);

        ImageExists.ShouldBeTrue();
    }

    [Fact]
    public void An_image_another_open_document_references_is_left_alone()
    {
        // Two documents in one folder can share an image, and the second one's claim is as good
        // as the first's.
        Review("# Guide\n", savedText: "", others: $"see ![]({Reference})");

        ImageExists.ShouldBeTrue();
    }

    [Fact]
    public void Cutting_the_paragraph_to_move_it_recycles_and_pasting_it_back_restores()
    {
        // The clipboard round trip an author makes constantly. It has to survive.
        Review("# Guide\n");
        Review($"# Guide\n\nmoved down\n\n![]({Reference})");

        ImageExists.ShouldBeTrue();
    }

    [Fact]
    public void An_untracked_file_in_the_same_folder_is_never_touched()
    {
        string stranger = Path.Combine(_folder.Path, "guide.assets", "not-ours.png");
        File.WriteAllBytes(stranger, [9]);

        Review("# Guide\n");

        // Only files this session wrote are ever in play. Anything that was already there
        // belongs to the user and is none of this class's business.
        File.Exists(stranger).ShouldBeTrue();
    }

    [Fact]
    public void A_reference_is_matched_without_regard_to_case()
    {
        Review($"![]({Reference.ToUpperInvariant()})");

        ImageExists.ShouldBeTrue();
    }

    [Fact]
    public void Cleaning_up_empties_the_recycle_folder()
    {
        Review("# Guide\n");
        Directory.EnumerateFiles(_paths.RecycleDirectory).ShouldNotBeEmpty();

        _tracker.CleanUp();

        Directory.Exists(_paths.RecycleDirectory).ShouldBeFalse();
    }

    [Fact]
    public void Two_images_with_the_same_name_from_different_folders_do_not_collide()
    {
        string otherPath = Path.Combine(_folder.Path, "notes.assets", "image.png");

        Directory.CreateDirectory(Path.GetDirectoryName(otherPath)!);
        File.WriteAllBytes(otherPath, [7, 7, 7]);

        var second = Guid.NewGuid();
        _tracker.Track(second, "notes.assets/image.png", otherPath);

        Review("");
        _ = _tracker.ReviewAsync(second, "", "", []).GetAwaiter().GetResult();

        // Both recycled under distinct names, so restoring one cannot hand back the other's bytes.
        _ = _tracker.ReviewAsync(second, "![](notes.assets/image.png)", "", []).GetAwaiter().GetResult();

        File.ReadAllBytes(otherPath).ShouldBe([7, 7, 7]);
        ImageExists.ShouldBeFalse();
    }

    public void Dispose()
    {
        _tracker.CleanUp();
        _folder.Dispose();
    }
}
