// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace PaulTechGuy.MQ.Docx;

/// <summary>
/// Puts text into a run the way Word expects to find it.
///
/// Two characters cannot simply sit inside <c>w:t</c>. A soft hyphen (U+00AD, <c>&amp;shy;</c>)
/// there is drawn by Word as an ordinary hyphen, every time, so the fixture's
/// <c>super&amp;shy;calif&amp;shy;ragilistic</c> printed as <c>super-calif-ragilistic</c>. Word's own
/// optional hyphen is the <c>w:softHyphen</c> element, which shows only where a line breaks. A
/// non-breaking hyphen (U+2011) likewise has an element of its own, <c>w:noBreakHyphen</c>.
/// Everything else goes through <see cref="XmlSafeText.Clean"/> as before.
/// </summary>
internal static class RunText
{
    /// <summary>Appends <paramref name="text"/> to <paramref name="run"/>, after any properties.</summary>
    public static void AppendTo(Run run, string text)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(text);

        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            OpenXmlElement? hyphen = text[i] switch
            {
                '­' => new SoftHyphen(),
                '‑' => new NoBreakHyphen(),
                _ => null,
            };

            if (hyphen is null)
            {
                continue;
            }

            if (i > start)
            {
                AppendPlain(run, text[start..i]);
            }

            run.AppendChild(hyphen);
            start = i + 1;
        }

        // The tail, or the whole of a text with neither character in it. An empty text still
        // gets its w:t, as it always has.
        if (start < text.Length || start == 0)
        {
            AppendPlain(run, text[start..]);
        }
    }

    private static void AppendPlain(Run run, string text) =>
        run.AppendChild(new Text(XmlSafeText.Clean(text))
        {
            Space = SpaceProcessingModeValues.Preserve,
        });
}
