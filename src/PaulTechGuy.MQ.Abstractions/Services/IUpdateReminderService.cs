// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// The clock behind "time to check for updates".
///
/// It is a clock and nothing more. Marqora asks GitHub nothing, so it cannot know whether a
/// newer release exists - only how long it has been since it last suggested the reader go and
/// look. Everything here reads or writes two dates in the settings file; the one address it
/// exposes is handed to the reader's browser by the caller, which is the only way any URL
/// leaves the app.
///
/// That is what keeps the sentence on the front of the README literally true rather than
/// nearly true. A check that quietly fetched a version number would be a network call however
/// small it was, and no amount of "but it sends nothing" would make the claim honest again.
///
/// Every method is handed the current time rather than reading it, so the whole rule can be
/// tested without waiting a month or standing in a clock abstraction.
/// </summary>
public interface IUpdateReminderService
{
    /// <summary>
    /// The releases page. Never fetched here - the caller hands it to the shell.
    ///
    /// GitHub resolves it to whichever release is newest, so nothing in Marqora has to know a
    /// version number it could only have learned over the network.
    /// </summary>
    string ReleasesUrl { get; }

    /// <summary>
    /// Starts the clock for this session. Called once at launch.
    ///
    /// Two things start it. A settings file with no date at all, which is a fresh install -
    /// and no reminder is owed to someone who installed the newest release this morning. And a
    /// recorded version that is not the one running, which means the reader has updated since
    /// the last reminder and should not be nudged again a week later.
    ///
    /// Separate from <see cref="IsDue"/> so that the one which writes and the one which only
    /// reads are told apart at the call site.
    /// </summary>
    void Start(DateTimeOffset now);

    /// <summary>
    /// Whether a reminder is owed. Pure: asking changes nothing.
    ///
    /// False whenever the interval is zero, which is how the preference switches the feature
    /// off, and false until <see cref="Start"/> has put a date on the clock.
    ///
    /// True when the clock reads backwards as well as when it has run out. A machine whose
    /// date is corrected from 2031 down to today would otherwise sit years short of its next
    /// reminder. Firing once - after which <see cref="MarkReminded"/> writes a sane date - is
    /// the cheaper end of that mistake than going silent until 2031 comes round for real.
    /// </summary>
    bool IsDue(DateTimeOffset now);

    /// <summary>
    /// Records that the reader has just been pointed at the releases page, restarting the
    /// interval.
    ///
    /// Called when the reminder appears, and when Help, Check for Updates is used. Showing it
    /// is what spends it: a reminder that stayed due because it was ignored would be back
    /// again tomorrow, which is the difference between a reminder and a nag.
    /// </summary>
    void MarkReminded(DateTimeOffset now);

    /// <summary>
    /// How long since the last reminder, or null before there has been one.
    ///
    /// For the About box, which reports what the app is doing rather than acting on it.
    /// </summary>
    TimeSpan? Elapsed(DateTimeOffset now);
}
