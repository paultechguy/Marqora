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

    public DocumentFolder() => Directory.CreateDirectory(_root);

    /// <summary>The document being analyzed. It does not have to exist on disk.</summary>
    public string DocumentPath => Path.Combine(_root, "doc.md");

    /// <summary>Creates a neighbouring file for a link to point at.</summary>
    public DocumentFolder With(string relativePath, string contents = "")
    {
        string full = Path.Combine(_root, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);

        return this;
    }

    /// <summary>Analyzes markdown as a saved document in this folder.</summary>
    public IReadOnlyList<Diagnostic> Check(string markdown) => Analyze(markdown, DocumentPath);

    /// <summary>Analyzes markdown as a document that has never been saved anywhere.</summary>
    public static IReadOnlyList<Diagnostic> CheckUnsaved(string markdown) => Analyze(markdown, null);

    private static IReadOnlyList<Diagnostic> Analyze(string markdown, string? path)
    {
        RenderedMarkdown rendered = Renderer.Render(markdown);

        return Analyzer.Analyze(new AnalysisRequest
        {
            Text = markdown,
            DocumentPath = path,
            Links = rendered.Links,
            Outline = rendered.Outline,
            Anchors = rendered.Anchors,
        });
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the test run is not worth failing over.
        }
    }
}
