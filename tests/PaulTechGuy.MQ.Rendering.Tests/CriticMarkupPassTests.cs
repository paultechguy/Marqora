// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Rendering.Tests;

/// <summary>
/// A reviewed document pasted back in: the CriticMarkup Copy as Markdown writes is drawn as
/// comments - the passage classed as one, the note gathered - and never as a yellow highlight
/// with a stray brace at each end.
/// </summary>
public sealed class CriticMarkupPassTests
{
    private static readonly MarkdigMarkdownRenderer Renderer =
        new(NullLogger<MarkdigMarkdownRenderer>.Instance);

    private static string Html(string markdown) => Renderer.Render(markdown).Html;

    /// <summary>The case that was reported: a passage across bold and code, and a note with code in it.</summary>
    [Fact]
    public void A_commented_passage_with_formatting_is_one_comment_and_its_note_is_gathered()
    {
        string html = Html("{==A run cleared **6 videos** at `woz-u` today.==}{>>done do `this`.<<}\n");

        html.ShouldContain("<mark class=\"mq-critic\">A run cleared <strong>6 videos</strong> at <code>woz-u</code> today.</mark>");
        html.ShouldContain("<span class=\"mq-critic-note\">done do <code>this</code>.</span>");
        html.ShouldNotContain("{");
        html.ShouldNotContain("}");
        html.ShouldNotContain("&lt;&lt;");
    }

    /// <summary>A standalone note - the form a comment takes when its passage could not be placed.</summary>
    [Fact]
    public void A_standalone_note_is_a_note() =>
        Html("{>>On \"x\": Retry?<<}\n").ShouldContain("<span class=\"mq-critic-note\">On &quot;x&quot;: Retry?</span>");

    /// <summary>An ordinary ==highlight== is still the author's highlight, and not a comment.</summary>
    [Fact]
    public void An_ordinary_highlight_is_left_alone()
    {
        string html = Html("Keep ==this== yellow.\n");

        html.ShouldContain("<mark>this</mark>");
        html.ShouldNotContain("mq-critic");
    }

    /// <summary>Markup with no partner means what it says.</summary>
    [Fact]
    public void An_unclosed_note_is_left_as_written() =>
        Html("Say {>>something\n").ShouldContain("Say {&gt;&gt;something");

    /// <summary>Two comments on one line are two comments.</summary>
    [Fact]
    public void Two_comments_on_one_line_are_both_read()
    {
        string html = Html("{==a==}{>>one<<} and {==b==}{>>two<<}\n");

        html.ShouldContain("<mark class=\"mq-critic\">a</mark><span class=\"mq-critic-note\">one</span> and ");
        html.ShouldContain("<mark class=\"mq-critic\">b</mark><span class=\"mq-critic-note\">two</span>");
    }

    /// <summary>Inside code, CriticMarkup is literal text, as everything is.</summary>
    [Fact]
    public void Markup_inside_code_is_untouched() =>
        Html("Write `{==x==}{>>y<<}` to comment.\n").ShouldContain("<code>{==x==}{&gt;&gt;y&lt;&lt;}</code>");
}
