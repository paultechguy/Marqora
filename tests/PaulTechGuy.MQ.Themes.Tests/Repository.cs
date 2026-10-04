// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using Xunit;

namespace PaulTechGuy.MQ.Themes.Tests;

/// <summary>
/// The files beside the code that the theme tests read: the webshell's stylesheets, and in
/// them the two colors no theme owns.
/// </summary>
internal static partial class Repository
{
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PaulTechGuy.MQ.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be found.");
    }

    public static string Webshell(string file) => Path.Combine(Root(), "webshell", file);

    /// <summary>
    /// The page and body-text colors for a mode, as app.css states them.
    ///
    /// Read rather than written into the tests, because app.css is where the page is chosen and
    /// a copy here would be a second place to change. Light is the :root block at column zero;
    /// dark is the first <c>:root[data-theme="dark"]</c> block, which precedes the print rules.
    /// </summary>
    public static (string Page, string BodyText) Neutrals(PaletteMode mode)
    {
        string css = File.ReadAllText(Webshell("app.css"));

        Match header = mode == PaletteMode.Dark ? DarkRoot().Match(css) : LightRoot().Match(css);

        Assert.True(header.Success, $"app.css has no {mode} :root block.");

        return (Declared(css, header.Index, "--mq-bg"), Declared(css, header.Index, "--mq-text"));
    }

    private static string Declared(string css, int from, string property)
    {
        Match match = new Regex($@"{Regex.Escape(property)}\s*:\s*(#[0-9a-fA-F]{{6}})\s*;").Match(css, from);

        Assert.True(match.Success, $"app.css declares no {property} after offset {from}.");

        return match.Groups[1].Value.ToLowerInvariant();
    }

    [GeneratedRegex(@"^:root\s*\{", RegexOptions.Multiline)]
    private static partial Regex LightRoot();

    [GeneratedRegex(@":root\[data-theme=""dark""\]\s*\{")]
    private static partial Regex DarkRoot();
}
