// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Folio.Tests;

/// <summary>
/// Anchors and links, once many documents have become one page.
///
/// Every one of these fails invisibly if it is wrong: the page still renders, and the mistake
/// only shows when somebody clicks and lands on the wrong document.
/// </summary>
public sealed class FolioLinksTests
{
    private static readonly Dictionary<string, string> Anchors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["guide.md"] = "mq-doc-1",
        ["setup.md"] = "mq-doc-2",
    };

    [Fact]
    public void Heading_anchors_take_their_document_prefix()
    {
        string html = FolioLinks.Namespace(
            "<h2 id=\"introduction\">Introduction</h2>", ["introduction"], "mq-doc-3");

        html.ShouldBe("<h2 id=\"mq-doc-3-introduction\">Introduction</h2>");
    }

    /// <summary>
    /// Two documents with the same heading is the whole reason this exists, and an id shared
    /// between them means the contents list lands on whichever came first.
    /// </summary>
    [Fact]
    public void The_same_heading_in_two_documents_gets_two_anchors()
    {
        const string Source = "<h1 id=\"overview\">Overview</h1>";

        FolioLinks.Namespace(Source, ["overview"], FolioLinks.Anchor(0))
            .ShouldBe("<h1 id=\"mq-doc-1-overview\">Overview</h1>");

        FolioLinks.Namespace(Source, ["overview"], FolioLinks.Anchor(1))
            .ShouldBe("<h1 id=\"mq-doc-2-overview\">Overview</h1>");
    }

    [Fact]
    public void A_shorter_slug_does_not_rename_a_longer_one()
    {
        string html = FolioLinks.Namespace(
            "<h2 id=\"intro\">A</h2><h2 id=\"introduction\">B</h2>",
            ["intro", "introduction"],
            "d");

        html.ShouldBe("<h2 id=\"d-intro\">A</h2><h2 id=\"d-introduction\">B</h2>");
    }

    /// <summary>
    /// Mermaid's SVG refers to its own arrow markers with url(#...). Renaming those ids without
    /// renaming the references strips the arrowheads off every flowchart in the Folio.
    /// </summary>
    [Fact]
    public void Ids_that_are_not_heading_slugs_are_left_alone()
    {
        const string Diagram =
            "<svg><marker id=\"arrowhead-1\"/><path marker-end=\"url(#arrowhead-1)\"/></svg>";

        FolioLinks.Namespace(Diagram, ["overview"], "mq-doc-1").ShouldBe(Diagram);
    }

    [Fact]
    public void A_link_within_the_document_gains_the_same_prefix_as_its_heading()
    {
        string html = FolioLinks.Relink(
            "<a href=\"#install\">Install</a>", Anchors, "mq-doc-2", ["install"]);

        html.ShouldBe("<a href=\"#mq-doc-2-install\">Install</a>");
    }

    [Fact]
    public void A_link_to_another_document_becomes_that_documents_anchor()
    {
        string html = FolioLinks.Relink(
            "<a href=\"setup.md\">Setup</a>", Anchors, "mq-doc-1", []);

        html.ShouldBe("<a href=\"#mq-doc-2\">Setup</a>");
    }

    /// <summary>
    /// The case most easily got backwards: the fragment names a heading in the document being
    /// linked *to*, so it takes that document's prefix and not the linking document's.
    /// </summary>
    [Fact]
    public void A_link_to_a_heading_in_another_document_takes_that_documents_prefix()
    {
        string html = FolioLinks.Relink(
            "<a href=\"setup.md#install\">Install</a>", Anchors, "mq-doc-1", ["install"]);

        html.ShouldBe("<a href=\"#mq-doc-2-install\">Install</a>");
    }

    [Fact]
    public void A_percent_encoded_destination_is_recognized()
    {
        Dictionary<string, string> anchors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["my guide.md"] = "mq-doc-4",
        };

        string html = FolioLinks.Relink(
            "<a href=\"my%20guide.md\">Guide</a>", anchors, "mq-doc-1", []);

        html.ShouldBe("<a href=\"#mq-doc-4\">Guide</a>");
    }

    [Fact]
    public void A_link_that_leaves_the_folio_is_untouched()
    {
        const string Html =
            "<a href=\"https://example.com\">Out</a><a href=\"../notes/scratch.md\">Notes</a>";

        FolioLinks.Relink(Html, Anchors, "mq-doc-1", []).ShouldBe(Html);
    }

    [Fact]
    public void Anchors_are_one_based_because_they_are_read()
    {
        FolioLinks.Anchor(0).ShouldBe("mq-doc-1");
        FolioLinks.Anchor(11).ShouldBe("mq-doc-12");
    }
}
