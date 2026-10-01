// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// The decisions behind following a relative link out of the preview, apart from the part
/// that actually launches anything.
/// </summary>
public static class LocalLinks
{
    /// <summary>
    /// File types Windows will run rather than open.
    ///
    /// A document can come from anyone, and a link reads as whatever its label says: "the
    /// release notes" can point at setup.exe. Clicking one of these therefore asks first, naming
    /// the file, whatever the preference says. The list errs long - a question too many costs
    /// a click, and one too few runs a program.
    /// </summary>
    private static readonly HashSet<string> RunnableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".appinstaller", ".application", ".appref-ms", ".appx", ".appxbundle", ".bat", ".chm",
        ".cmd", ".com", ".cpl", ".diagcab", ".exe", ".gadget", ".hta", ".inf", ".ins", ".isp",
        ".jar", ".js", ".jse", ".library-ms", ".lnk", ".msc", ".msi", ".msix", ".msixbundle",
        ".msp", ".mst", ".pif", ".ps1", ".ps1xml", ".ps2", ".ps2xml", ".psc1", ".psc2", ".psd1",
        ".psm1", ".reg", ".scf", ".scr", ".sct", ".settingcontent-ms", ".shb", ".shs", ".url",
        ".vb", ".vbe", ".vbs", ".ws", ".wsc", ".wsf", ".wsh", ".xll",
    };

    /// <summary>
    /// Whether opening this file would run it.
    ///
    /// Trailing dots and spaces are ignored the way Windows ignores them, or "setup.exe." would
    /// report no extension here and still start setup.exe when launched.
    /// </summary>
    public static bool IsRunnable(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return RunnableExtensions.Contains(Path.GetExtension(path.TrimEnd('.', ' ')));
    }

    /// <summary>
    /// The heading a link's anchor names, or null when there is none.
    ///
    /// An exact match wins. After that case is ignored, because GitHub's slugs are lower case
    /// and people write the anchor by hand from the heading they can see.
    /// </summary>
    public static OutlineHeading? FindHeading(IReadOnlyList<OutlineHeading> outline, string fragment)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (string.IsNullOrEmpty(fragment))
        {
            return null;
        }

        return outline.FirstOrDefault(h => string.Equals(h.Slug, fragment, StringComparison.Ordinal))
            ?? outline.FirstOrDefault(h => string.Equals(h.Slug, fragment, StringComparison.OrdinalIgnoreCase));
    }
}
