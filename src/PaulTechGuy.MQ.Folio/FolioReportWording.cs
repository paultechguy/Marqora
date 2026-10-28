// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Folio;

/// <summary>
/// The Folio report's wording, shared by the app's export report and marqora-cli's folio findings
/// (CLI-FO-06), so the two say the same thing about the same warning. Moved out of MainViewModel.
/// </summary>
public static class FolioReportWording
{
    /// <summary>
    /// A Folio warning as the report's left-hand phrase: what went wrong, in a form somebody can
    /// act on, sentence case because it reads as a heading of its own.
    ///
    /// Deliberately not the warning's own message. That sentence is written for the preflight,
    /// where it argues for a decision the author has not made yet; by the time this window is up
    /// the decision is made and the file is written, so the row says what the artifact lacks.
    /// </summary>
    public static string Problem(FolioWarningKind kind) => kind switch
    {
        FolioWarningKind.MissingImage => "Not on this machine",
        FolioWarningKind.OutsideLink => "Points outside the Folio",
        FolioWarningKind.NotRewritable => "Reference could not be repointed",
        FolioWarningKind.WillBeShrunk => "Sent smaller than the original",
        FolioWarningKind.RemoteImageNotIncluded => "Left on the web",
        FolioWarningKind.RemoteImageFailed => "Could not be fetched",
        // Names the person and the consequence, because the earlier wording - "loads from the
        // web when opened" - left both to be guessed at: loads what, opened by whom. A reader
        // of this report is deciding whether to send the file to somebody, and the thing they
        // need to know is that the somebody will end up fetching from that site.
        FolioWarningKind.RemoteMediaNotIncluded => "Whoever opens this fetches it from the web",
        _ => "Not carried across",
    };

    /// <summary>
    /// What it happened to. The address for anything on the web, because that is the thing a
    /// reader has to go and look at - and unlike a Word export these are never data URIs, so
    /// they are short enough to read.
    /// </summary>
    public static string Item(FolioWarning warning) =>
        warning.Kind == FolioWarningKind.RemoteImageFailed
            ? $"{warning.Url} - {FailureReason(warning.Message)}"
            : warning.Url;

    /// <summary>
    /// The tail of a fetch failure's message, which is the half that says why.
    ///
    /// The message is built for a list read on its own and leads with the address; repeating it
    /// beside the address column would be noise.
    /// </summary>
    public static string FailureReason(string message)
    {
        int colon = message.IndexOf(": ", StringComparison.Ordinal);

        return colon > 0 && colon + 2 < message.Length ? message[(colon + 2)..] : message;
    }
}
