// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging.Abstractions;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Rendering;

namespace PaulTechGuy.MQ.Folio.Tests;

/// <summary>
/// A throwaway tree holding documents, their images, and somewhere outside them both.
///
/// Real files, because what is being tested is whether a path resolves and whether two files
/// hold the same bytes. Substituting the filesystem would leave nothing worth asserting on.
///
/// Documents go through the real renderer, so every rewrite is aimed at the line and column
/// Markdig reports rather than at one the test made up.
/// </summary>
internal sealed class FolioWorkspace : IDisposable
{
    private static readonly MarkdigMarkdownRenderer Renderer =
        new(NullLogger<MarkdigMarkdownRenderer>.Instance);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "marqora-folio-tests", Guid.NewGuid().ToString("n"));

    private readonly List<FolioSource> _sources = [];

    public FolioWorkspace()
    {
        Directory.CreateDirectory(Docs);
        Directory.CreateDirectory(Elsewhere);
    }

    /// <summary>Where the documents live.</summary>
    public string Docs => Path.Combine(_root, "docs");

    /// <summary>Somewhere off the documents' tree entirely, standing in for a Pictures folder.</summary>
    public string Elsewhere => Path.Combine(_root, "elsewhere");

    /// <summary>Writes a file anywhere under the tree, creating folders as needed.</summary>
    public string File(string relativePath, string contents)
    {
        string full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, contents);

        return full;
    }

    /// <summary>Adds a document to the Folio, rendering it to collect its links.</summary>
    public FolioWorkspace Document(string relativePath, string text)
    {
        string full = File(relativePath, text);

        _sources.Add(new FolioSource(full, text, Renderer.Render(text).Links));

        return this;
    }

    public FolioPlan Plan() => FolioPlanner.Plan(_sources);

    /// <summary>The planned text of one document, by the name it was added under.</summary>
    public static string TextOf(FolioPlan plan, string fileName) =>
        plan.Documents.Single(d => Path.GetFileName(d.SourcePath) == fileName).Text;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the test is not a failed test.
        }
    }
}
