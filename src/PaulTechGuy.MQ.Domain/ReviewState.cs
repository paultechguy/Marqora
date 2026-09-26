// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaulTechGuy.MQ.Domain;

/// <summary>One comment as a shared page carries it for resuming: its anchor and its note.</summary>
public sealed record ReviewStateComment
{
    public Guid Id { get; set; }

    public int Line { get; set; }

    public int Index { get; set; }

    public int Start { get; set; }

    public int End { get; set; }

    public string Quote { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;
}

/// <summary>Why a page could not be resumed, for the one sentence the reader is told.</summary>
public enum ReviewStateProblem
{
    None,

    /// <summary>No state block: not a review page, or one shared before resuming existed.</summary>
    Missing,

    /// <summary>The block is there but does not read: truncated, edited, or not base64 JSON.</summary>
    Damaged,

    /// <summary>A later Marqora wrote it and said an earlier one must not read it.</summary>
    TooNew,

    /// <summary>Larger than any review this app would write.</summary>
    TooLarge,

    /// <summary>It reads, but what it says does not hold together: the text or an anchor is wrong.</summary>
    Inconsistent,
}

/// <summary>
/// What a shared review page carries so that the reviewer can pick it up again: the exact
/// text that was reviewed and every comment with its anchor.
///
/// The CriticMarkup block beside it cannot do this job. It is for people and AIs to read, and
/// it is not reversible: a comment that could not be placed inline becomes a standalone note,
/// and its escaping of <c>&lt;script</c> is lossy. This block is for Marqora alone, so it is
/// base64 - for the reason a Folio's payload is: nothing the document says can end the element.
///
/// A page is a file somebody sent. Everything read back here is checked before any of it is
/// used, and a page that fails a check is refused whole rather than half-restored.
///
/// The version is not a gate, as with a Folio: a page from a later Marqora is read for what
/// this build understands, and the generated reader skips fields it does not know.
/// <see cref="MinimumReader"/> is the gate a later format sets on purpose, when it adds
/// something an earlier build must not drop. docs/Review.md has the design.
/// </summary>
public sealed record ReviewState
{
    public const string FormatName = "marqora-review";

    public const int CurrentSchemaVersion = 1;

    /// <summary>The reader version this build is; a page whose <see cref="MinimumReader"/> is higher is refused.</summary>
    public const int ReaderVersion = 1;

    /// <summary>Inert, like a Folio's payload: an unknown script type is neither run nor shown.</summary>
    public const string ScriptType = "application/vnd.marqora.review+json";

    /// <summary>The id the block is found by.</summary>
    public const string StateId = "mq-review-state";

    /// <summary>The name of the head's marker meta, which makes recognizing a review page a cheap read.</summary>
    public const string Marker = "marqora-review";

    /// <summary>
    /// The largest block read, in base64 characters. Forty-eight million is 36 MB of JSON,
    /// several times a very long document with thousands of comments.
    /// </summary>
    public const int MaximumEncodedLength = 48_000_000;

    public const int MaximumComments = 5_000;

    /// <summary>The longest note or quote accepted. A comment is a sentence or a paragraph, not a book.</summary>
    public const int MaximumTextLength = 100_000;

    private const int LongestFileName = 120;

    public string Format { get; set; } = FormatName;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public int MinimumReader { get; set; } = ReaderVersion;

    public string? AppVersion { get; set; }

    /// <summary>The review, across every page it has been shared to and every sitting it has been resumed in.</summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// New on every share. Two copies of one review share a session id and even a count of
    /// shares, so this is what tells "the page I wrote" from "a page another copy wrote".
    /// </summary>
    public Guid WriteId { get; set; }

    public int ShareCount { get; set; }

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset SharedUtc { get; set; }

    /// <summary>The reviewed document's file name, "notes.md" - not a path, and never the tab's label.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>The full SHA-256 of <see cref="Source"/>, lower-case hex.</summary>
    public string SourceSha256 { get; set; } = string.Empty;

    /// <summary>The text that was reviewed, exactly.</summary>
    public string Source { get; set; } = string.Empty;

    public IReadOnlyList<ReviewStateComment> Comments { get; set; } = [];

    /// <summary>Whether a later Marqora wrote this, so overwriting it here would drop what it added.</summary>
    [JsonIgnore]
    public bool IsNewerSchema => SchemaVersion > CurrentSchemaVersion;

    /// <summary>The meta that goes in the head, beside the generator.</summary>
    public static string MarkerMeta => $"<meta name=\"{Marker}\" content=\"1\" />";

    /// <summary>The state of a session as it is being shared.</summary>
    public static ReviewState For(
        ReviewSession session,
        string fileName,
        Guid writeId,
        int shareCount,
        DateTimeOffset sharedUtc,
        string? appVersion)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new ReviewState
        {
            AppVersion = appVersion,
            SessionId = session.SessionId,
            WriteId = writeId,
            ShareCount = shareCount,
            StartedUtc = session.StartedUtc.ToUniversalTime(),
            SharedUtc = sharedUtc.ToUniversalTime(),
            FileName = SanitizeFileName(fileName),
            SourceSha256 = Sha256(session.SourceText),
            Source = session.SourceText,
            Comments = [.. session.Ordered.Select(c => new ReviewStateComment
            {
                Id = c.Id,
                Line = c.Anchor.Line,
                Index = c.Anchor.Index,
                Start = c.Anchor.Start,
                End = c.Anchor.End,
                Quote = c.Anchor.Quote,
                Note = c.Note,
            })],
        };
    }

    /// <summary>
    /// The script element that ends a shared page. It must be the last element in the body: see
    /// <see cref="TryDecode(string, out ReviewStateProblem)"/> for why the reader counts on that.
    /// </summary>
    public static string Encode(ReviewState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        string json = JsonSerializer.Serialize(state, ReviewStateJsonContext.Default.ReviewState);
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        return $"<script type=\"{ScriptType}\" id=\"{StateId}\">{encoded}</script>";
    }

    public static ReviewState? TryDecode(string html) => TryDecode(html, out _);

    /// <summary>
    /// The state out of a shared page, or null with the reason.
    ///
    /// Found by the last occurrence, not the first. A document can hold raw HTML, so its own
    /// text could carry a block that looks exactly like this one, and that would land in the
    /// article - before the real block, which the writer puts last. Nothing the document says
    /// can stand after the real one: the CriticMarkup block between them escapes every
    /// <c>&lt;script</c>, so the last match is the one Marqora wrote.
    /// </summary>
    public static ReviewState? TryDecode(string html, out ReviewStateProblem problem)
    {
        ArgumentNullException.ThrowIfNull(html);

        int opening = html.LastIndexOf($"<script type=\"{ScriptType}\" id=\"{StateId}\">", StringComparison.OrdinalIgnoreCase);

        if (opening < 0)
        {
            problem = ReviewStateProblem.Missing;
            return null;
        }

        int start = html.IndexOf('>', opening) + 1;
        int end = html.IndexOf("</script>", start, StringComparison.OrdinalIgnoreCase);

        if (start <= 0 || end < start)
        {
            problem = ReviewStateProblem.Damaged;
            return null;
        }

        if (end - start > MaximumEncodedLength)
        {
            problem = ReviewStateProblem.TooLarge;
            return null;
        }

        ReviewState? state;

        try
        {
            byte[] json = Convert.FromBase64String(html[start..end].Trim());

            state = JsonSerializer.Deserialize(json, ReviewStateJsonContext.Default.ReviewState);
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            problem = ReviewStateProblem.Damaged;
            return null;
        }

        if (state is null || !string.Equals(state.Format, FormatName, StringComparison.Ordinal))
        {
            problem = ReviewStateProblem.Damaged;
            return null;
        }

        if (state.MinimumReader > ReaderVersion)
        {
            problem = ReviewStateProblem.TooNew;
            return null;
        }

        if (!HoldsTogether(state))
        {
            problem = ReviewStateProblem.Inconsistent;
            return null;
        }

        state.FileName = SanitizeFileName(state.FileName);
        problem = ReviewStateProblem.None;

        return state;
    }

    /// <summary>
    /// Whether what the page says is consistent: the text is the text its hash names, and every
    /// anchor points somewhere that text has. An anchor that points nowhere would draw nothing,
    /// or an empty highlight, and a comment would be restored that the reader could not find.
    /// </summary>
    private static bool HoldsTogether(ReviewState state)
    {
        if (state.Source is null
            || state.Comments is null
            || state.SessionId == Guid.Empty
            || !string.Equals(state.SourceSha256, Sha256(state.Source), StringComparison.OrdinalIgnoreCase)
            || state.Comments.Count > MaximumComments)
        {
            return false;
        }

        int lines = 1;

        foreach (char c in state.Source)
        {
            if (c == '\n')
            {
                lines++;
            }
        }

        HashSet<Guid> ids = [];

        foreach (ReviewStateComment comment in state.Comments)
        {
            if (comment is null
                || comment.Id == Guid.Empty
                || !ids.Add(comment.Id)
                || comment.Line < 0
                || comment.Line >= lines
                || comment.Index < 0
                || comment.Start < 0
                || comment.Start >= comment.End
                || string.IsNullOrEmpty(comment.Quote)
                || comment.Quote.Length > MaximumTextLength
                || comment.Note is null
                || comment.Note.Length > MaximumTextLength)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the head announces a review page that can be resumed. Cheap: the head alone.</summary>
    public static bool IsReviewPage(string head)
    {
        ArgumentNullException.ThrowIfNull(head);

        return head.Contains($"name=\"{Marker}\"", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the head is a review page shared before resuming existed - Marqora 1.0.10 and
    /// earlier - which carries the comments but not what is needed to resume them. Only for
    /// wording the refusal, so it is judged from the head: Marqora's generator meta and a title
    /// ending "— Review", which is how every review page has been titled.
    /// </summary>
    public static bool IsLegacyReviewPage(string head)
    {
        ArgumentNullException.ThrowIfNull(head);

        return !IsReviewPage(head)
            && head.Contains("<meta name=\"generator\" content=\"Marqora\"", StringComparison.OrdinalIgnoreCase)
            && head.Contains(" — Review</title>", StringComparison.Ordinal);
    }

    /// <summary>
    /// A file name from a page, made safe to show and to suggest: no folder, no control or
    /// direction-override characters (a right-to-left override can make "notes\u202Egpj.exe" read as
    /// something else), and no longer than a tab can sensibly hold.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        string name = string.Empty;

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            try
            {
                name = Path.GetFileName(fileName.Replace('\\', '/').Split('/')[^1]);
            }
            catch (ArgumentException)
            {
                name = string.Empty;
            }
        }

        var builder = new StringBuilder(name.Length);

        foreach (char c in name)
        {
            bool bidi = c is (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069') or '\u200E' or '\u200F' or '\u061C';

            if (!char.IsControl(c) && !bidi && Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0)
            {
                builder.Append(c);
            }
        }

        string clean = builder.ToString().Trim().TrimEnd('.');

        if (clean.Length > LongestFileName)
        {
            string extension = Path.GetExtension(clean);

            clean = extension.Length is > 0 and < 16
                ? clean[..(LongestFileName - extension.Length)] + extension
                : clean[..LongestFileName];
        }

        return clean.Length == 0 ? "Untitled.md" : clean;
    }

    /// <summary>The full SHA-256 of the text as UTF-8, lower-case hex.</summary>
    public static string Sha256(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReviewState))]
internal sealed partial class ReviewStateJsonContext : JsonSerializerContext;
