// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace PaulTechGuy.MQ.Domain;

/// <summary>Why a setting in an imported file did not arrive intact.</summary>
public enum PreferenceImportIssue
{
    /// <summary>The file named a setting this build has never heard of - it came from a newer Marqora.</summary>
    Unrecognised = 0,

    /// <summary>The key is known but the value could not be read as one: a misspelt enum, a string where a number belongs.</summary>
    Rejected = 1,

    /// <summary>This build has the setting and the file did not mention it - the file came from an older Marqora.</summary>
    NotInFile = 2,

    /// <summary>The value was outside the range the setting allows and was brought back into it.</summary>
    Adjusted = 3,

    /// <summary>The file carried session state - open documents, window position - which import never applies.</summary>
    SessionIgnored = 4,
}

/// <summary>One line of the import report.</summary>
/// <param name="Issue">What happened.</param>
/// <param name="Detail">Said in the words the report shows, already naming the settings involved.</param>
public sealed record PreferenceImportNote(PreferenceImportIssue Issue, string Detail);

/// <summary>
/// What came of reading a preferences file.
///
/// Import is best-effort by design: a file from a different build is the ordinary case, not
/// an error, so anything this build understands is applied and the rest is reported. That
/// makes the report the substance of the result rather than a footnote - a half-applied
/// import that said nothing would look exactly like one that applied cleanly.
///
/// <see cref="Settings"/> is the merged record, not something already in force. The caller
/// applies it, which is what keeps import an ordinary change that Cancel can undo like any
/// other.
/// </summary>
public sealed record PreferencesImportResult
{
    /// <summary>Why the file could not be used at all, or null when it could.</summary>
    public string? Failure { get; init; }

    /// <summary>The preferences to apply, with this machine's session state left in place.</summary>
    public AppSettings? Settings { get; init; }

    public bool Succeeded => Failure is null && Settings is not null;

    /// <summary>Settings the file offered that this build recognised and accepted.</summary>
    public int Applied { get; init; }

    /// <summary>Settings the file offered in total, whether or not they landed.</summary>
    public int Offered { get; init; }

    /// <summary>The Marqora that wrote the file, if it said.</summary>
    public string? SourceAppVersion { get; init; }

    /// <summary>The envelope version the file declared, or zero when it had no envelope.</summary>
    public int SourceSchemaVersion { get; init; }

    /// <summary>
    /// True when the file was a raw settings.json rather than an exported preferences file.
    ///
    /// Copying settings.json off another machine is the obvious thing to try, so it is
    /// accepted - but it is worth saying out loud, because such a file also holds session
    /// state that import silently left behind.
    /// </summary>
    public bool WasRawSettingsFile { get; init; }

    public IReadOnlyList<PreferenceImportNote> Notes { get; init; } = [];

    /// <summary>
    /// True when something in the file did not arrive as it was written.
    ///
    /// Not the same as "there were notes". Two of the five kinds are the feature working as
    /// designed rather than anything going wrong: a file from an older build is missing keys
    /// this one has, and a settings.json carries session state that import is right to leave
    /// behind. Neither is worth putting a warning colour on a report, and treating them as
    /// one would put a warning on almost every cross-version import - which is the ordinary
    /// case, and the case the feature exists for.
    ///
    /// What counts is a setting the file had an opinion about that this build could not honour:
    /// one it does not recognise, one it could not read, or one it had to move into range.
    /// </summary>
    public bool IsPartial => Notes.Any(note => note.Issue
        is PreferenceImportIssue.Unrecognised
        or PreferenceImportIssue.Rejected
        or PreferenceImportIssue.Adjusted);

    public static PreferencesImportResult Failed(string reason) => new() { Failure = reason };

    /// <summary>
    /// The report, as the dialog shows it: one headline sentence, then a line per issue.
    ///
    /// Built here rather than in the dialog because it is the part worth testing, and because
    /// the wording is a statement about what import did rather than a matter of layout.
    /// </summary>
    public string Describe()
    {
        if (Failure is { } failure)
        {
            return failure;
        }

        var text = new StringBuilder();

        text.Append(Applied == Offered
            ? $"Applied {Applied} {Plural(Applied, "preference", "preferences")}."
            : $"Applied {Applied} of {Offered} preferences.");

        if (SourceAppVersion is { Length: > 0 } version)
        {
            text.Append(CultureInfo.CurrentCulture, $" The file was written by Marqora {version}.");
        }

        if (WasRawSettingsFile)
        {
            text.Append(" It was a settings.json rather than an exported preferences file.");
        }

        foreach (PreferenceImportNote note in Notes)
        {
            text.Append("\n\n").Append(note.Detail);
        }

        return text.ToString();
    }

    private static string Plural(int count, string one, string many) => count == 1 ? one : many;
}
