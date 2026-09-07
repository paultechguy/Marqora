// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Services.Tests;

/// <summary>
/// When Marqora suggests you go and look for a new release, and when it stays quiet.
///
/// The whole feature is two dates and a subtraction, which is exactly why it is worth testing
/// here: the cases that matter are a machine that slept for a month, a clock that was put
/// back, and a fresh install that owes nobody a reminder - none of which anyone would find by
/// running the app for an afternoon.
///
/// Every method takes the time rather than reading it, so a month passes between two lines.
/// </summary>
public sealed class UpdateReminderTests
{
    private const string Version = "1.2.3";

    private static readonly DateTimeOffset Noon = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSettingsService _settings = new();

    private UpdateReminderService ServiceFor(string version = Version) =>
        new(_settings, version, NullLogger<UpdateReminderService>.Instance);

    private AppSettings Current => _settings.Current;

    // ------------------------------------------------------------------- starting

    /// <summary>
    /// A fresh install has never been reminded, and is running the newest release there is.
    /// Starting the clock rather than firing is the difference between a reminder and a
    /// greeting.
    /// </summary>
    [Fact]
    public void Start_OnAFreshInstall_RecordsTheDateAndDoesNotFire()
    {
        UpdateReminderService service = ServiceFor();

        service.IsDue(Noon).ShouldBeFalse();

        service.Start(Noon);

        Current.LastUpdateReminderUtc.ShouldBe(Noon);
        Current.LastUpdateReminderVersion.ShouldBe(Version);
        service.IsDue(Noon).ShouldBeFalse();
    }

    /// <summary>
    /// The ordinary launch: the clock is already running and must not be reset, or the
    /// reminder would be a month away from every launch and so never arrive.
    /// </summary>
    [Fact]
    public void Start_WhenTheClockIsAlreadyRunning_LeavesItAlone()
    {
        UpdateReminderService service = ServiceFor();
        service.Start(Noon);

        service.Start(Noon.AddDays(20));

        Current.LastUpdateReminderUtc.ShouldBe(Noon);
        service.IsDue(Noon.AddDays(31)).ShouldBeTrue();
    }

    /// <summary>
    /// Somebody who updated last week does not need reminding this week. The recorded version
    /// differing from the running one is the only evidence of an update Marqora has, and it
    /// costs nothing to act on.
    /// </summary>
    [Fact]
    public void Start_AfterAnUpdate_RestartsTheClock()
    {
        ServiceFor().Start(Noon);

        UpdateReminderService updated = ServiceFor("1.3.0");
        DateTimeOffset later = Noon.AddDays(29);

        updated.Start(later);

        Current.LastUpdateReminderUtc.ShouldBe(later);
        Current.LastUpdateReminderVersion.ShouldBe("1.3.0");
        updated.IsDue(later.AddDays(29)).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ falling due

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(400, true)]
    public void IsDue_AnswersTheInterval(int days, bool expected)
    {
        UpdateReminderService service = ServiceFor();
        service.Start(Noon);

        service.IsDue(Noon.AddDays(days)).ShouldBe(expected);
    }

    /// <summary>Zero is how the preference switches the whole thing off.</summary>
    [Fact]
    public void IsDue_WithTheIntervalAtZero_NeverFires()
    {
        UpdateReminderService service = ServiceFor();
        service.Start(Noon);

        _settings.Update(s => s with { UpdateReminderDays = 0 });

        service.IsDue(Noon.AddYears(5)).ShouldBeFalse();
    }

    /// <summary>
    /// The machine that was six weeks in the future and has just been put right. Comparing
    /// dates rather than counting down is what makes this answerable at all, and firing once
    /// is the cheaper end of the mistake than going silent until the clock catches up.
    /// </summary>
    [Fact]
    public void IsDue_WhenTheClockHasBeenPutBack_FiresOnce()
    {
        UpdateReminderService service = ServiceFor();
        service.Start(Noon.AddDays(42));

        service.IsDue(Noon).ShouldBeTrue();

        service.MarkReminded(Noon);

        service.IsDue(Noon).ShouldBeFalse();
        service.IsDue(Noon.AddDays(30)).ShouldBeTrue();
    }

    /// <summary>
    /// The case the feature exists for: a session left open across a month, where a timer set
    /// for the interval would have been asleep for most of it.
    /// </summary>
    [Fact]
    public void IsDue_AcrossALongUptime_FiresOnEveryInterval()
    {
        UpdateReminderService service = ServiceFor();
        service.Start(Noon);

        service.IsDue(Noon.AddDays(30)).ShouldBeTrue();
        service.MarkReminded(Noon.AddDays(30));

        service.IsDue(Noon.AddDays(45)).ShouldBeFalse();
        service.IsDue(Noon.AddDays(60)).ShouldBeTrue();
    }

    // ------------------------------------------------------------------- recording

    /// <summary>
    /// Showing the reminder spends it. One that stayed due because it was ignored would be
    /// back tomorrow, which is the difference between a reminder and a nag.
    /// </summary>
    [Fact]
    public void MarkReminded_RestartsTheIntervalAndStampsTheVersion()
    {
        UpdateReminderService service = ServiceFor();
        service.Start(Noon);

        DateTimeOffset shown = Noon.AddDays(30);
        service.MarkReminded(shown);

        Current.LastUpdateReminderUtc.ShouldBe(shown);
        Current.LastUpdateReminderVersion.ShouldBe(Version);
        service.IsDue(shown.AddDays(29)).ShouldBeFalse();
    }

    // -------------------------------------------------------------------- elapsed

    [Fact]
    public void Elapsed_IsNullBeforeTheClockStarts()
    {
        ServiceFor().Elapsed(Noon).ShouldBeNull();
    }

    [Fact]
    public void Elapsed_CountsFromTheLastReminder()
    {
        UpdateReminderService service = ServiceFor();
        service.Start(Noon);

        service.Elapsed(Noon.AddDays(12))!.Value.Days.ShouldBe(12);
    }

    /// <summary>Never negative: a clock that has been put back would otherwise report the future.</summary>
    [Fact]
    public void Elapsed_WithTheClockPutBack_IsZeroRatherThanNegative()
    {
        UpdateReminderService service = ServiceFor();
        service.Start(Noon.AddDays(42));

        service.Elapsed(Noon).ShouldBe(TimeSpan.Zero);
    }

    // ---------------------------------------------------------------------- the link

    /// <summary>
    /// The one address the whole feature knows. Nothing fetches it - the caller hands it to
    /// the browser - but a mistyped one would ship a dead link in a status bar.
    /// </summary>
    [Fact]
    public void ReleasesUrl_IsTheProjectsReleasesPage()
    {
        string url = ServiceFor().ReleasesUrl;

        ProjectLinks.IsUsable(url).ShouldBeTrue();
        url.ShouldBe(ProjectLinks.LatestReleaseUrl);
    }
}
