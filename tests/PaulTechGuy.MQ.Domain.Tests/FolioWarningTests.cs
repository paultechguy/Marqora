// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Domain.Tests;

/// <summary>
/// Which findings are faults and which are only worth knowing.
///
/// Pinned here because two windows read it - the preflight before the share and the report
/// after it - and they have to agree. They disagreed once: the preflight called every finding a
/// problem while the report counted the same set as two failures and a note, which left the
/// author deciding which of the two to believe.
///
/// A theory over every value rather than a handful of examples, so a kind added later has to be
/// classified on purpose. Forgetting is what this is for.
/// </summary>
public sealed class FolioWarningTests
{
    private static FolioWarning Of(FolioWarningKind kind) => new()
    {
        Kind = kind,
        DocumentPath = @"C:\docs\guide.md",
        Line = 0,
        Url = "x.png",
        Message = "message",
    };

    [Theory]
    [InlineData(FolioWarningKind.MissingImage)]
    [InlineData(FolioWarningKind.OutsideLink)]
    [InlineData(FolioWarningKind.NotRewritable)]
    [InlineData(FolioWarningKind.RemoteImageFailed)]
    public void Something_broken_is_a_fault(FolioWarningKind kind) =>
        Of(kind).IsAdvisory.ShouldBeFalse();

    [Theory]
    [InlineData(FolioWarningKind.WillBeShrunk)]
    [InlineData(FolioWarningKind.RemoteImageNotIncluded)]
    [InlineData(FolioWarningKind.RemoteMediaNotIncluded)]
    public void Something_working_as_designed_is_only_worth_knowing(FolioWarningKind kind) =>
        Of(kind).IsAdvisory.ShouldBeTrue();

    [Fact]
    public void Every_kind_is_classified_by_one_of_the_two_lists_above()
    {
        // The guard that makes the theories complete. A kind added without a decision about it
        // falls into neither list above and is caught here rather than in a window, months
        // later, miscounted.
        FolioWarningKind[] decided =
        [
            FolioWarningKind.MissingImage,
            FolioWarningKind.OutsideLink,
            FolioWarningKind.NotRewritable,
            FolioWarningKind.RemoteImageFailed,
            FolioWarningKind.WillBeShrunk,
            FolioWarningKind.RemoteImageNotIncluded,
            FolioWarningKind.RemoteMediaNotIncluded,
        ];

        Enum.GetValues<FolioWarningKind>().ShouldBe(decided, ignoreOrder: true);
    }

    [Fact]
    public void A_plan_counts_the_two_apart()
    {
        var plan = new FolioPlan
        {
            Documents = [],
            Assets = [],
            Warnings =
            [
                Of(FolioWarningKind.MissingImage),
                Of(FolioWarningKind.OutsideLink),
                Of(FolioWarningKind.RemoteMediaNotIncluded),
            ],
        };

        plan.FailureCount.ShouldBe(2);
        plan.AdvisoryCount.ShouldBe(1);
    }
}
