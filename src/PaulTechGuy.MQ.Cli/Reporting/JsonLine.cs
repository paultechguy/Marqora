// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaulTechGuy.MQ.Cli.Reporting;

/// <summary>The theme an export or Folio wore, and which rung of the ladder chose it (CLI-TH-06).</summary>
internal sealed record JsonTheme(string Id, string Source);

/// <summary>One heading of outline's --json: its level, its text as the outline shows it, its anchor and its one-based line.</summary>
internal sealed record JsonHeading(int Level, string Text, string Anchor, int Line);

/// <summary>
/// folio --dry-run's preflight, line for line (CLI-FO-05): the form, the documents, the pictures
/// and how many were collected from elsewhere, what is missing or points outside, the estimate in
/// bytes with any advice, and - only under --fetch-web-images - the sites that would be contacted.
/// Each problem is also a finding on its document's own line.
/// </summary>
internal sealed record JsonPreflight(
    string Form,
    IReadOnlyList<string> Documents,
    int Pictures,
    int Collected,
    int MissingPictures,
    int OutsideLinks,
    int WebPictures,
    long EstimatedBytes,
    string? Advice,
    IReadOnlyList<string>? Sites);

/// <summary>
/// One input's outcome under --json: a line of its own on stdout, so a script reads a partial
/// failure file by file without parsing the messages on stderr (CLI-RPT-04).
///
/// The schema is versioned (CLI-RPT-05). A field may be added and the version stays; a field
/// removed, renamed or given a new meaning raises it. Golden tests compare every verb's lines
/// against a recorded snapshot of the version-1 shape, so a rename fails a build rather than a
/// user's script.
/// </summary>
internal sealed record JsonLine
{
    /// <summary>The version of this shape. Raised only by a change that is not purely additive.</summary>
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },

        // Paths and messages carry backslashes, quotes and accents; written plainly they are what
        // a person reading the line expects, and still valid JSON.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The input as it was named, or "-" for standard input.</summary>
    public required string Path { get; init; }

    /// <summary>The verb, by its canonical name: "format", "export pdf".</summary>
    public required string Verb { get; init; }

    /// <summary>What happened to this input: see <see cref="Outcomes"/>.</summary>
    public required string Outcome { get; init; }

    /// <summary>Every file written for this input. Empty when nothing was.</summary>
    public IReadOnlyList<string> Outputs { get; init; } = [];

    public IReadOnlyList<DocumentFinding> Findings { get; init; } = [];

    /// <summary>The one-sentence message for a failure; absent on success.</summary>
    public string? Message { get; init; }

    /// <summary>The folder relative links resolved against, for standard input only (CLI-IN-03).</summary>
    public string? BaseDir { get; init; }

    /// <summary>The theme in force, on exports and the Folio.</summary>
    public JsonTheme? Theme { get; init; }

    /// <summary>The document a text verb produced, when it printed one rather than writing a file.</summary>
    public string? Text { get; init; }

    /// <summary>The unified diff --dry-run printed for this input.</summary>
    public string? Diff { get; init; }

    /// <summary>outline's heading tree.</summary>
    public IReadOnlyList<JsonHeading>? Headings { get; init; }

    /// <summary>folio --dry-run's preflight, on the line for the Folio itself.</summary>
    public JsonPreflight? Preflight { get; init; }

    public string Serialize() => JsonSerializer.Serialize(this, Options);
}

/// <summary>The values <see cref="JsonLine.Outcome"/> takes. Part of the schema: never renamed.</summary>
internal static class Outcomes
{
    /// <summary>A text verb's result was printed to stdout.</summary>
    public const string Printed = "printed";

    /// <summary>--write rewrote the file.</summary>
    public const string Written = "written";

    /// <summary>The verb would change nothing; the file was left as it was.</summary>
    public const string Unchanged = "unchanged";

    /// <summary>--check or --dry-run: the file would change, and was not touched.</summary>
    public const string WouldChange = "wouldChange";

    /// <summary>A report verb ran and found nothing it counts against the file.</summary>
    public const string Passed = "passed";

    /// <summary>A report verb ran and found what fails the run: check's errors.</summary>
    public const string Found = "found";

    /// <summary>The verb produced an output file.</summary>
    public const string Exported = "exported";

    /// <summary>Nothing could be done with this input; <see cref="JsonLine.Message"/> says why.</summary>
    public const string Failed = "failed";

    /// <summary>Not reached: an earlier file failed under --fail-fast, or the run was canceled.</summary>
    public const string Skipped = "skipped";
}
