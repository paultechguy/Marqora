// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Click a diagram in an exported page and it opens on its own, clear of the document, with no
/// script in the file.
///
/// A mermaid diagram is laid out by mermaid's own useMaxWidth default, which writes
/// <c>width="100%"</c> and an inline <c>max-width</c> of the diagram's natural width. The result
/// is fit-to-column: a diagram wider than the text shrinks until it fits, labels and all. In the
/// app that is fine, because double-clicking one opens it in a window that zooms and pans. A file
/// that has left the app has no such window, and browser zoom is no answer either - it shrinks
/// the column by exactly as much as it magnifies, so a fit-to-width diagram comes out the same
/// physical size while the prose around it grows.
///
/// So the diagram is promoted to a full-viewport overlay by <c>:target</c>. The fragment in the
/// address bar is the open/closed state and the browser is what keeps it. No script means the
/// export stays a static document - which is what makes it safe to mail, and what keeps it out
/// of arguments with a reader's virus scanner.
///
/// <b>This views a diagram, it does not magnify one, and the label says so.</b> The overlay fits
/// the whole diagram to the viewport, enlarging as readily as shrinking - the same rule the
/// pop-out window's fit uses. A small diagram therefore grows a great deal. A large one, which
/// was already fitted to the column and is usually tall as well as wide, comes out about the
/// size it was: fitting all of it on screen is the constraint, and no arrangement of CSS beats
/// it. What the reader gains there is the diagram alone on its own paper, which is worth having
/// and is not the same promise as "bigger".
///
/// Going further would mean zoom and pan, and zoom and pan mean script. That is the line this
/// deliberately does not cross.
///
/// The markup and the stylesheet are one mechanism and have to agree about class names, so they
/// are stated together here rather than the CSS living with the page layout.
///
/// <b>What :target cannot do.</b> Escape does not close the overlay, because nothing is
/// listening for a key. Browser Back does close it, and every open leaves a history entry. Both
/// are the price of having no script at all.
/// </summary>
internal static partial class DiagramViewer
{
    /// <summary>
    /// Wraps every rendered diagram in the self-linking anchor, plus the sheet that closes it.
    ///
    /// The identifier is a running count rather than the diagram's own hash. The hash is of the
    /// definition, so a document that draws the same diagram twice - or a Folio where two
    /// documents do - would give two elements the same id, and the second would never open.
    /// </summary>
    public static string Wrap(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        int count = 0;

        return DiagramBlock().Replace(html, match =>
        {
            string id = string.Create(CultureInfo.InvariantCulture, $"mq-view-{++count}");

            // Adjacent with no whitespace between them: the exit sheet is selected as the
            // anchor's next sibling, and a text node there would not break that, but the two
            // belong together and reading them apart invites someone to separate them.
            return string.Create(
                CultureInfo.InvariantCulture,
                $"<a class=\"mq-view\" id=\"{id}\" href=\"#{id}\" aria-label=\"View this diagram on its own\">"
                + $"{match.Value}</a>"
                + $"<a class=\"mq-view-exit\" href=\"#{id}-closed\" aria-label=\"Close this diagram\"></a>");
        });
    }

    /// <summary>
    /// The stylesheet, or nothing at all for a document with no diagrams in it.
    ///
    /// Written the way <see cref="RenderedHtmlPackager.ReadStyles"/> decides on the math and
    /// highlighting themes: a page that cannot use a block of rules should not carry it.
    /// </summary>
    /// <param name="wrappedHtml">The markup <see cref="Wrap"/> has already been over.</param>
    public static string CssFor(string wrappedHtml)
    {
        ArgumentNullException.ThrowIfNull(wrappedHtml);

        return wrappedHtml.Contains("class=\"mq-view\"", StringComparison.Ordinal) ? Css : string.Empty;
    }

    /// <summary>
    /// Matches one rendered diagram. The content is inline SVG, which cannot itself contain
    /// a closing pre tag, so the lazy match to the first one is exact rather than hopeful.
    /// </summary>
    [GeneratedRegex(
        @"<pre\b[^>]*\bdata-mq-diagram=\x22[^\x22]*\x22[^>]*>.*?</pre>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DiagramBlock();

    private const string Css = """
        /* ------------------------------------------ click-to-view (no script) */

        .mq-preview a.mq-view {
          display: block;
          border-bottom: none;
          color: inherit;
          cursor: pointer;
        }

        /*
          app.css offers "Double-click to open", which is the app's pop-out window and is not in
          this file. Outranked rather than switched off: the affordance was right, only the
          sentence was wrong.
        */
        .mq-preview .mq-view pre.mermaid[data-mq-diagram]::after {
          content: "Click to view";
        }

        .mq-preview a.mq-view-exit {
          display: none;
        }

        /*
          The backdrop is the overlay's own background rather than a ::before behind it. An
          element already covering the viewport has nowhere better to paint, and a pseudo element
          at a negative z-index depends on the stacking context around it being what you assumed
          - which is a thing to be sure of in someone else's browser, not clever.
        */
        .mq-preview a.mq-view:target {
          position: fixed;
          inset: 0;
          z-index: 901;
          display: flex;
          align-items: center;
          justify-content: center;
          padding: 2rem;
          background: rgba(16, 19, 22, 0.88);
          cursor: pointer;
        }

        /*
          The diagram gets its own opaque card, and this is the part that matters rather than the
          dimming. A sequence diagram is almost entirely transparent - boxes and lifelines over
          nothing - so a page dimmed to 12 percent still reads straight through the middle of one.
          Paper under it is what separates the diagram from the document.
        */
        .mq-preview a.mq-view:target pre.mermaid {
          margin: 0;
          padding: 1.5rem;
          max-width: none;
          background: var(--mq-bg);
          border: none;
          border-radius: 12px;
          outline: none;
          overflow: visible;
          box-shadow: 0 24px 64px rgba(0, 0, 0, 0.45);
        }

        /* The hint has done its job by the time the diagram is open. */
        .mq-preview a.mq-view:target pre.mermaid[data-mq-diagram]::after {
          content: none;
        }

        /*
          Fit the whole diagram to the viewport, enlarging as readily as shrinking.

          Mermaid writes its natural-width cap as an inline style, which no stylesheet rule
          outranks without !important, and that cap is what would hold a small diagram down.
          Height is the definite axis and width follows the viewBox ratio; max-width then clamps
          a wide diagram and the height re-derives from the same ratio. The 8rem is the overlay's
          padding and the card's, so neither edge is pushed off screen.
        */
        .mq-preview a.mq-view:target pre.mermaid svg {
          width: auto;
          height: calc(100vh - 8rem);
          max-width: calc(100vw - 8rem) !important;
        }

        /*
          The transparent sheet that catches the click closing it. Above the diagram rather than
          only around it, so anywhere at all closes - with nothing listening for Escape, a reader
          who has to find the margin is a reader who is stuck.
        */
        .mq-preview .mq-view:target + a.mq-view-exit {
          display: block;
          position: fixed;
          inset: 0;
          z-index: 902;
          border-bottom: none;
          cursor: pointer;
        }

        @media print {
          .mq-preview a.mq-view { cursor: auto; }

          .mq-preview .mq-view pre.mermaid[data-mq-diagram]::after { content: none; }

          .mq-preview a.mq-view-exit { display: none; }
        }
        """;
}
