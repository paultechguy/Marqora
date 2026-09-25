// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The parts of a shared review page that are text rather than the preview: its stylesheet, the
/// stamp at the top, and the block that carries the reviewed source.
///
/// A review page is the rendered preview as the reviewer saw it, with each comment highlighted
/// and its note in the margin beside it. Nothing in it runs. The margin notes are floats, the
/// numbers are links, and the pairing between a highlight and its note on hover is
/// <c>:has()</c> - so the page reads the same in a mail client that strips script as in a
/// browser, and still works in five years. The markup around each comment is built by the shell,
/// which is the only side that can place a note in the page; this class supplies everything
/// that does not need the page to exist.
/// </summary>
public static partial class ReviewPage
{
    /// <summary>The script type of the source block. Inert: a browser neither runs it nor shows it.</summary>
    public const string SourceType = "text/markdown";

    /// <summary>The id a reader, or a tool, finds the source block by.</summary>
    public const string SourceId = "mq-review-source";

    /// <summary>
    /// The stylesheet a review page adds to the export's own.
    ///
    /// The text column keeps the reader's measure and the notes take a column of their own to its
    /// right, reserved as padding on the article so they cannot overlap the text. The article is
    /// made content-box for this one purpose: app.css makes everything border-box, under which
    /// the padding would come out of the measure instead of being added beside it.
    ///
    /// Below the width where both columns fit, the notes stop floating and sit in the text,
    /// right after the passage they are about, which is how they read on a phone.
    /// </summary>
    /// <param name="measurePixels">The export's text measure, or 0 for none.</param>
    public static string Css(int measurePixels)
    {
        string stampWidth = measurePixels > 0
            ? string.Create(CultureInfo.InvariantCulture, $"max-width: calc({measurePixels}px + var(--mq-note-width) + var(--mq-note-gap));")
            : "max-width: none;";

        return $$"""
            .mq-review {
              --mq-note-width: 17rem;
              --mq-note-gap: 2rem;
              --mq-review-accent: var(--mq-accent, #3f8f98);
              --mq-review-mark: color-mix(in srgb, var(--mq-review-accent) 20%, transparent);
              --mq-review-mark-hot: color-mix(in srgb, var(--mq-review-accent) 40%, transparent);
              --mq-review-note: color-mix(in srgb, var(--mq-review-accent) 7%, #ffffff);
              --mq-review-note-hot: color-mix(in srgb, var(--mq-review-accent) 18%, #ffffff);
            }

            .mq-review .mq-preview {
              box-sizing: content-box;
              padding-right: calc(var(--mq-note-width) + var(--mq-note-gap));
            }

            .mq-review-stamp {
              {{stampWidth}}
              margin: 0 auto 1.5rem;
              display: flex;
              align-items: center;
              gap: 0.75rem;
              font-size: 0.9375rem;
              line-height: 1.35;
              color: var(--mq-text, #1f2328);
            }

            .mq-review-stamp .mq-review-logo { flex: none; width: 2.75rem; height: 2.75rem; }
            .mq-review-stamp .mq-review-lines { min-width: 0; }
            .mq-review-stamp .mq-review-line { display: block; overflow-wrap: anywhere; }
            .mq-review-stamp .mq-review-note { color: var(--mq-text-secondary, #656d76); }

            .mq-review .mq-preview mark.mq-comment {
              background: var(--mq-review-mark);
              color: inherit;
              border-radius: 0;
              padding: 0 1px;
              border-bottom: 2px solid var(--mq-review-accent);
              scroll-margin-top: 4rem;
            }

            /* Inside inline code, rounded to nest in the code box's own corners, as the preview has it. */
            .mq-review .mq-preview :not(pre) > code mark.mq-comment { border-radius: 3px; }

            .mq-review a.mq-comment-ref {
              font-size: 0.75em;
              font-weight: 700;
              line-height: 0;
              vertical-align: super;
              margin-left: 1px;
              color: var(--mq-review-accent);
              text-decoration: none;
            }

            .mq-review .mq-sidenote {
              float: right;
              clear: right;
              position: relative;
              width: var(--mq-note-width);
              margin: 0.15rem calc(-1 * (var(--mq-note-width) + var(--mq-note-gap))) 0.75rem 0;
              padding: 0.5rem 0.7rem;
              background: var(--mq-review-note);
              border-left: 3px solid var(--mq-review-accent);
              border-radius: 0 4px 4px 0;
              font-size: 0.8125rem;
              font-style: normal;
              font-weight: normal;
              line-height: 1.45;
              text-align: left;
              text-transform: none;
              letter-spacing: normal;
              color: var(--mq-text, #1f2328);
              scroll-margin-top: 4rem;
            }

            .mq-review .mq-sidenote-number {
              margin-right: 0.35rem;
              font-weight: 700;
              color: var(--mq-review-accent);
              text-decoration: none;
            }

            .mq-review .mq-sidenote-para { display: block; margin-top: 0.45rem; }

            .mq-review .mq-note-host { height: 0; }

            .mq-review .mq-preview mark.mq-comment:target { background: var(--mq-review-mark-hot); }
            .mq-review .mq-sidenote:target,
            .mq-review .mq-sidenote:hover { background: var(--mq-review-note-hot); }
            .mq-review .mq-sidenote:target { outline: 2px solid var(--mq-review-accent); }

            @media (max-width: 62rem) {
              .mq-review .mq-preview { padding-right: 0; }
              .mq-review-stamp { max-width: none; }

              .mq-review .mq-sidenote {
                float: none;
                display: block;
                width: auto;
                margin: 0.5rem 0 0.75rem;
              }

              .mq-review .mq-note-host { height: auto; }
            }
            """;
    }

    /// <summary>
    /// Hover pairs a highlight with its note, in both directions, for every comment.
    ///
    /// By number rather than by adjacency. A note cannot always sit beside its highlight - one in
    /// a table cell is hosted before the table, because a float cannot leave a cell - and a
    /// sibling selector only reaches the ones that do. <c>:has()</c> asks the page as a whole,
    /// so every pair works the same way wherever its note ended up.
    /// </summary>
    public static string HoverCss(int count)
    {
        var builder = new StringBuilder();

        for (int n = 1; n <= count; n++)
        {
            builder.Append(CultureInfo.InvariantCulture, $".mq-review:has(mark.mq-comment[data-note=\"{n}\"]:hover) #mq-note-{n} {{ background: var(--mq-review-note-hot); }}\n");
            builder.Append(CultureInfo.InvariantCulture, $".mq-review:has(#mq-note-{n}:hover) mark.mq-comment[data-note=\"{n}\"] {{ background: var(--mq-review-mark-hot); }}\n");

            // The comment lit whole: one passage across bold or code words is several marks, and
            // :hover or :target on its own would light only the one under the pointer or the first.
            builder.Append(CultureInfo.InvariantCulture, $".mq-review:has(mark.mq-comment[data-note=\"{n}\"]:hover) mark.mq-comment[data-note=\"{n}\"] {{ background: var(--mq-review-mark-hot); }}\n");
            builder.Append(CultureInfo.InvariantCulture, $".mq-review:has(#mq-mark-{n}:target) mark.mq-comment[data-note=\"{n}\"] {{ background: var(--mq-review-mark-hot); }}\n");
        }

        return builder.ToString();
    }

    /// <summary>
    /// The header at the top of the page saying exactly which version was reviewed, so an author
    /// who has edited since knows the comments are about an earlier draft.
    ///
    /// The source hash is kept as an attribute rather than shown: it tells two drafts apart for
    /// a tool, and a reader has the date for that. A long file name is shortened in the text and
    /// given whole as the hover title.
    /// </summary>
    /// <param name="logoDataUri">The logo as a data URI, or null to leave it out.</param>
    public static string Stamp(string fileName, string reviewed, string shortHash, int count, string? logoDataUri = null)
    {
        string comments = count == 1 ? "1 comment" : string.Create(CultureInfo.InvariantCulture, $"{count} comments");
        string shortName = ShortFileName(fileName);
        string nameTitle = shortName == fileName ? string.Empty : " title=\"" + WebUtility.HtmlEncode(fileName) + "\"";
        string logo = string.IsNullOrEmpty(logoDataUri)
            ? string.Empty
            : "<img class=\"mq-review-logo\" src=\"" + WebUtility.HtmlEncode(logoDataUri) + "\" alt=\"Marqora\" />";

        return "<header class=\"mq-review-stamp\" data-source-sha256=\"" + WebUtility.HtmlEncode(shortHash) + "\">"
            + logo
            + "<div class=\"mq-review-lines\">"
            + "<span class=\"mq-review-line\">Review of <span" + nameTitle + ">" + WebUtility.HtmlEncode(shortName) + "</span>"
            + " • " + WebUtility.HtmlEncode(reviewed)
            + " • " + comments + "</span>"
            + "<span class=\"mq-review-line mq-review-note\">Later edits to the source are not reflected here.</span>"
            + "</div>"
            + "</header>";
    }

    /// <summary>The longest file name the header shows whole.</summary>
    private const int LongestShownName = 30;

    /// <summary>
    /// A file name short enough for the header: the start of the name, "...", then the last few
    /// characters and the extension, so both the beginning a reader recognizes and the end that
    /// tells versions apart survive. <c>qzT7maKb9xR2vL38dioseusoeJEHp8Nc4WdY6sF1jHa3Bg5Ue0Xi7Po2Zr6.md</c>
    /// becomes <c>qzT7maKb9xR2vL3...Po2Zr6.md</c>. Counted in text elements, so an emoji or an accented
    /// letter is never cut in half.
    /// </summary>
    public static string ShortFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        const int head = 15;
        const int tail = 6;
        const string ellipsis = "...";

        if (new StringInfo(fileName).LengthInTextElements <= LongestShownName)
        {
            return fileName;
        }

        string extension = Path.GetExtension(fileName);
        var stem = new StringInfo(fileName[..^extension.Length]);

        if (stem.LengthInTextElements <= head + ellipsis.Length + tail)
        {
            return fileName;
        }

        return stem.SubstringByTextElements(0, head)
            + ellipsis
            + stem.SubstringByTextElements(stem.LengthInTextElements - tail)
            + extension;
    }

    /// <summary>
    /// The reviewed source, with its comments written in, carried in the page for whoever wants
    /// markdown rather than a page - an AI asked to act on the comments, most often.
    ///
    /// Readable rather than encoded, unlike a Folio's payload: someone doing View Source should
    /// be able to read it, and an AI given the file should not need to decode anything. That
    /// means the text has to be kept from ending the element early, and <c>&lt;/script</c> is
    /// not the only way to do that. A <c>&lt;!--</c> followed later by <c>&lt;script</c> moves
    /// the parser into a state where the real closing tag no longer closes anything, so all
    /// three sequences have their angle bracket followed by a backslash. A reader turns
    /// <c>&lt;\</c> back into <c>&lt;</c>.
    /// </summary>
    public static string SourceBlock(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        string safe = DangerousInScript().Replace(markdown, m => "<\\" + m.Value[1..]);

        return "<script type=\"" + SourceType + "\" id=\"" + SourceId + "\" data-format=\"criticmarkup\">\n"
            + safe
            + (safe.EndsWith('\n') ? string.Empty : "\n")
            + "</script>";
    }

    /// <summary>
    /// The first seven hex digits of the text's SHA-256, lower case: enough to tell two drafts
    /// apart at a glance, which is all the stamp asks of it.
    /// </summary>
    public static string ShortHash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..7];
    }

    /// <summary>The page's own file name: "notes.md" is shared as "notes (review by Marqora).html".</summary>
    public static string FileName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return Path.GetFileNameWithoutExtension(displayName) + " (review by Marqora).html";
    }

    [GeneratedRegex(@"</script|<!--|<script", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DangerousInScript();
}
