// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Two dates in the settings file, and the rules for reading them against the wall clock.
///
/// The design point worth keeping is that this compares timestamps rather than counting down.
/// A countdown is wrong the moment the machine sleeps - a timer does not fire while suspended,
/// so a laptop shut for a fortnight comes back a fortnight late - and wrong again for the
/// session somebody leaves open for six weeks, which is the case the feature exists for. Two
/// dates and a subtraction are right whatever the machine did in between, and they survive the
/// app being closed without anything else having to be persisted.
///
/// <paramref name="appVersion"/> is passed in rather than read here for the same reason
/// <see cref="WelcomeDocumentService"/> takes it: the version belongs to the executable, and
/// this layer is a library that a test host also loads.
/// </summary>
public sealed class UpdateReminderService(
    ISettingsService settings,
    string appVersion,
    ILogger<UpdateReminderService> logger) : IUpdateReminderService
{
    public string ReleasesUrl => ProjectLinks.LatestReleaseUrl;

    public void Start(DateTimeOffset now)
    {
        AppSettings current = settings.Current;

        bool never = current.LastUpdateReminderUtc is null;

        bool updated = !string.Equals(
            current.LastUpdateReminderVersion, appVersion, StringComparison.Ordinal);

        if (!never && !updated)
        {
            return;
        }

        Record(now);

        logger.LogDebug(
            "The update reminder clock starts at {When} for {Version} ({Reason}).",
            now,
            appVersion,
            never ? "no date was recorded" : "the version changed");
    }

    public bool IsDue(DateTimeOffset now)
    {
        AppSettings current = settings.Current;

        if (current.UpdateReminderDays <= 0 || current.LastUpdateReminderUtc is not { } last)
        {
            return false;
        }

        // Backwards as well as overdue. See the interface for why a corrected clock is better
        // off firing once than going silent for years.
        return now < last || now - last >= TimeSpan.FromDays(current.UpdateReminderDays);
    }

    public void MarkReminded(DateTimeOffset now) => Record(now);

    public TimeSpan? Elapsed(DateTimeOffset now)
    {
        if (settings.Current.LastUpdateReminderUtc is not { } last)
        {
            return null;
        }

        // Never negative: a clock that has been put back would otherwise report the future.
        return now > last ? now - last : TimeSpan.Zero;
    }

    /// <summary>
    /// Writes both halves of the record together.
    ///
    /// The version is stamped alongside the date every time rather than only when it changes,
    /// so the pair can never disagree about which build was running when the clock was last
    /// touched - which is the question <see cref="Start"/> asks on the next launch.
    /// </summary>
    private void Record(DateTimeOffset now) => settings.Update(s => s with
    {
        LastUpdateReminderUtc = now,
        LastUpdateReminderVersion = appVersion,
    });
}
