// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Net;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Wraps one diagram's SVG in a standalone HTML file.
///
/// Nothing is fetched when that file is opened: what mermaid produced is self-contained
/// markup, and the few rules around it are inline. That is the whole difference between this
/// and <see cref="RenderedHtmlPackager"/>, which has stylesheets, fonts and images to gather
/// before a document can survive leaving the app.
///
/// White, whatever theme the window was wearing, for the same reason the print stylesheet
/// forces it: a diagram written to a file is on its way somewhere else, and a dark plate is
/// rarely what is wanted when it arrives.
/// </summary>
internal static class DiagramHtmlDocument
{
    public static string Build(string svg, string title)
    {
        ArgumentNullException.ThrowIfNull(svg);

        // Double braces mark the holes, so the CSS below can keep its own single braces.
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8" />
            <title>{{WebUtility.HtmlEncode(title)}}</title>
            <style>
              html, body { margin: 0; background: #ffffff; }

              body {
                display: flex;
                min-height: 100vh;
                box-sizing: border-box;
                padding: 24px;
                align-items: center;
                justify-content: center;
              }

              svg { max-width: 100%; height: auto; }
            </style>
            </head>
            <body>
            {{svg}}
            </body>
            </html>
            """;
    }
}
