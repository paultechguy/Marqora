// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Folio;

/// <summary>How much a piece of preflight advice matters: the app's InfoBar shows the one as a warning, the other as information.</summary>
public enum FolioAdviceLevel
{
    Information,
    Warning,
}

/// <summary>One piece of advice about a Folio's shape: a title and a sentence or two.</summary>
public sealed record FolioAdvice(FolioAdviceLevel Level, string Title, string Message);

/// <summary>
/// What the Folio preflight says about a set before it is written: what it will weigh, and when one
/// page is the wrong shape for it.
///
/// Moved out of the app's FolioWindow so that the window and marqora-cli's "folio --dry-run" read
/// the same figures and give the same advice (docs/CLI-Implementation-Plan.md, WP-M1-16, F10), and
/// so the CLI refuses an over-large single page at the size the app warns at (CLI-FO-02).
/// </summary>
public static class FolioPreflight
{
    /// <summary>
    /// Past this many pictures a single page is slow to open whatever it weighs - a browser has
    /// to lay every one of them out at once, and none of them stream because they are all inline.
    /// It is a layout problem before it is a size problem, which is why it is counted separately.
    /// </summary>
    public const int CrowdedPageImages = 150;

    /// <summary>Past this the one file is unwieldy to open, mail aside.</summary>
    public const long HeavyPageBytes = 50L * 1024 * 1024;

    /// <summary>
    /// What most mail still stops at. Not a reason to refuse anything - plenty of Folios never
    /// go near email - but the number the author is usually judging the total against.
    /// </summary>
    public const long MailLimitBytes = 25L * 1024 * 1024;

    /// <summary>
    /// How much reducible weight is worth mentioning on a Folio that is otherwise fine.
    ///
    /// In bytes rather than a count of images, so that one enormous photograph is noticed and
    /// four slightly-too-wide screenshots are not.
    /// </summary>
    public const long ReducibleBytes = 5L * 1024 * 1024;

    /// <summary>The width the shrink box opens on, and what most screens are no wider than.</summary>
    public const int DefaultMaxImageWidth = 1600;

    /// <summary>
    /// What the chosen form will actually weigh.
    ///
    /// The one file is not the sum of its parts: every image goes in as base64, which costs a
    /// third again. Showing the raw total there would understate the file by 33% and make the
    /// figure the author is judging against email useless.
    /// </summary>
    public static long Weight(FolioPlan plan, bool singleFile)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return singleFile ? plan.TotalBytes * 4 / 3 : plan.TotalBytes;
    }

    /// <summary>
    /// The pictures that shrinking would reduce: wider than the default width, and only when
    /// shrinking is not already on - offering it otherwise would do nothing.
    /// </summary>
    public static IReadOnlyList<FolioAsset> Oversized(FolioPlan plan, int maxImageWidth)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return maxImageWidth > 0 ? [] : [.. plan.Assets.Where(a => a.PixelWidth > DefaultMaxImageWidth)];
    }

    /// <summary>
    /// Says when one page is the wrong shape for this set, and offers the zip in the same breath.
    ///
    /// Two separate ceilings, because they are two different failures. A great many pictures is
    /// a layout problem - the browser lays every one of them out at once and none of them stream,
    /// because they are all inline - and it bites at a count rather than a size. Sheer weight is
    /// the other, and the mail limit below it is advice rather than a fault: plenty of Folios
    /// never go near email.
    ///
    /// Everything about one page being the wrong shape applies to one page only. Reducible
    /// weight is not like that - a photograph that is four times wider than any screen is just as
    /// wasteful in a zip - so that last piece of advice is not gated on the form.
    /// </summary>
    /// <returns>The advice, or null when there is none.</returns>
    public static FolioAdvice? Advice(FolioPlan plan, bool singleFile, int maxImageWidth)
    {
        ArgumentNullException.ThrowIfNull(plan);

        long bytes = Weight(plan, singleFile);
        string zip = Size(plan.TotalBytes);

        IReadOnlyList<FolioAsset> oversized = Oversized(plan, maxImageWidth);
        long oversizedBytes = oversized.Sum(a => a.Bytes);

        string alsoShrink = oversized.Count > 0
            ? $" {Count(oversized.Count, "image")} here {(oversized.Count == 1 ? "is" : "are")} "
                + $"wider than {DefaultMaxImageWidth} px and could be sent smaller."
            : string.Empty;

        return (singleFile, plan.Assets.Count) switch
        {
            (true, > CrowdedPageImages) => new FolioAdvice(
                FolioAdviceLevel.Warning,
                "That is a lot of pictures for one page",
                $"{plan.Assets.Count} images in a single file are slow to open, whatever it "
                + $"weighs - a browser lays them all out at once. A zip would be about {zip}."
                + alsoShrink),

            _ when singleFile && bytes > HeavyPageBytes => new FolioAdvice(
                FolioAdviceLevel.Warning,
                "This will be a big file",
                $"About {Size(bytes)} in one page, which is unwieldy to open and past what most "
                + $"mail will take. A zip of the same documents would be about {zip}."
                + alsoShrink),

            _ when singleFile && bytes > MailLimitBytes => new FolioAdvice(
                FolioAdviceLevel.Information,
                "Larger than most mail will accept",
                $"About {Size(bytes)}, and most mail stops at around 25 MB. It opens perfectly "
                + $"well; it may just need another way to send it. A zip would be about {zip}."
                + alsoShrink),

            /*
                Nothing is wrong with the total, but a lot of it is reducible.

                Without this the offer only ever appears on a Folio that is already in trouble,
                and the case that prompted it - one very large photograph in an otherwise small
                set - never trips any of the thresholds above. Weighed in bytes rather than
                counted, so a handful of slightly-too-wide screenshots stays quiet.
            */
            _ when oversizedBytes > ReducibleBytes && oversized.Count > 0 => new FolioAdvice(
                FolioAdviceLevel.Information,
                "Some images are larger than they need to be",
                $"{Count(oversized.Count, "image")} account for about {Size(oversizedBytes)} of "
                + $"this, at more than {DefaultMaxImageWidth} px wide - wider than most screens. "
                + "Sending them smaller would not change how they look."),

            _ => null,
        };
    }

    public static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    /// <summary>
    /// A size a person reads rather than a byte count. One decimal past a megabyte, because the
    /// question being asked of this number is whether it will go through email.
    /// </summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.0} MB"),
    };
}
