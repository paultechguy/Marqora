// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Rendering.Tests;

/// <summary>
/// Emoji come from <c>:name:</c> shortcodes and nothing else, as on GitHub. Markdig's typed
/// faces are off: they are built from markdown's own punctuation, and a colon written before
/// a closing <c>**</c> or a closing parenthesis was read as a face that ate the delimiter.
/// </summary>
public class EmojiTests
{
    private static readonly MarkdigMarkdownRenderer Renderer =
        new(NullLogger<MarkdigMarkdownRenderer>.Instance);

    private static string HtmlOf(string markdown) => Renderer.Render(markdown).Html;

    [Fact]
    public void A_shortcode_still_becomes_an_emoji() =>
        HtmlOf("Launch :rocket:").ShouldContain("\U0001F680");

    /// <summary>The line that was reported: ":*" read as a kiss, and the bold never closed.</summary>
    [Fact]
    public void A_colon_before_closing_bold_leaves_the_bold_intact()
    {
        string html = HtmlOf("**`appsettings.lms.user.json`:**");

        html.ShouldContain("<strong><code>appsettings.lms.user.json</code>:</strong>");
        html.ShouldNotContain("\U0001F617");
    }

    [Theory]
    [InlineData("see note:)", ":)")]
    [InlineData("sad :( face", ":(")]
    [InlineData("wink ;) here", ";)")]
    public void A_typed_face_stays_as_text(string markdown, string face) =>
        HtmlOf(markdown).ShouldContain(face);
}
