// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Abstractions.Analysis;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Rendering;

namespace PaulTechGuy.MQ.Analysis.Tests;

/// <summary>
/// A throwaway folder holding a document and whatever it links to.
///
/// The link checks are the one part of the analyzer that touches the disk, so they are
/// exercised against real files rather than a substitute: what is being tested is precisely
/// whether the path a link resolves to is there.
///
/// Documents are run through the real renderer, so the line and column each diagnostic
/// carries is the one Markdig actually reports, not one the test made up.
/// </summary>
internal sealed class DocumentFolder : IDisposable
{
    private static readonly IMarkdownAnalyzer Analyzer = new MarkdownAnalyzer();
    private static readonly MarkdigMarkdownRenderer Renderer =
        new(NullLogger<MarkdigMarkdownRenderer>.Instance);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "marqora-tests", Guid.NewGuid().ToString("n"));

    private string? _sibling;

    public DocumentFolder() => Directory.CreateDirectory(_root);

    /// <summary>The document being analyzed. It does not have to exist on disk.</summary>
    public string DocumentPath => Path.Combine(_root, "doc.md");

    /// <summary>
    /// Creates a file in a neighbouring folder and returns its full path, for the references an
    /// author writes absolutely - "C:\Users\paul\Pictures\shot.png" and the like.
    /// </summary>
    public string WithAbsoluteSibling(string relativePath, string contents = "")
    {
        _sibling = _root + "2";

        string full = Path.Combine(_sibling, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);

        return full;
    }

    /// <summary>The full path of a file inside the document's own folder.</summary>
    public string PathTo(string relativePath) => Path.Combine(_root, relativePath);

    /// <summary>Creates a neighbouring file for a link to point at.</summary>
    public DocumentFolder With(string relativePath, string contents = "")
    {
        string full = Path.Combine(_root, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);

        return this;
    }

    /// <summary>
    /// Creates a file in a neighbouring folder whose name begins with this one's, and returns
    /// the relative path a document here would write to reach it.
    ///
    /// The shared prefix is the whole point: a containment check that compares bare strings
    /// reads "…\abc2\logo.png" as living inside "…\abc", so a link that leaves the document's
    /// folder passes a test meant to refuse it.
    /// </summary>
    public string WithPrefixedSibling(string relativePath, string contents = "")
    {
        _sibling = _root + "2";

        string full = Path.Combine(_sibling, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);

        return $"../{Path.GetFileName(_sibling)}/{relativePath.Replace('\\', '/')}";
    }

    /// <summary>The style rules found in markdown treated as a saved document in this folder.</summary>
    public IReadOnlyList<Diagnostic> Check(string markdown) => Analyze(markdown, DocumentPath).Diagnostics;

    /// <summary>The style rules found in markdown that has never been saved anywhere.</summary>
    public static IReadOnlyList<Diagnostic> CheckUnsaved(string markdown, bool altText = true) =>
        Analyze(markdown, null, altText).Diagnostics;

    /// <summary>The dead links found in markdown treated as a saved document in this folder.</summary>
    public IReadOnlyList<LinkFinding> Links(string markdown, bool blocked = true) =>
        Analyze(markdown, DocumentPath, blockedImages: blocked).LinkFindings;

    /// <summary>The dead links found in markdown that has never been saved anywhere.</summary>
    public static IReadOnlyList<LinkFinding> LinksUnsaved(string markdown, bool altText = true) =>
        Analyze(markdown, null, altText).LinkFindings;

    private static AnalysisResult Analyze(
        string markdown,
        string? path,
        bool altText = true,
        bool blockedImages = true)
    {
        RenderedMarkdown rendered = Renderer.Render(markdown);

        return Analyzer.Analyze(new AnalysisRequest
        {
            Text = markdown,
            DocumentPath = path,
            Links = rendered.Links,
            Outline = rendered.Outline,
            Anchors = rendered.Anchors,
            CheckImageAltText = altText,
            CheckBlockedImages = blockedImages,
        });
    }

    public void Dispose()
    {
        Remove(_root);

        if (_sibling is not null)
        {
            Remove(_sibling);
        }
    }

    private static void Remove(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the test run is not worth failing over.
        }
    }
}
