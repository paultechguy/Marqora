// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using MarkdigDocument = Markdig.Syntax.MarkdownDocument;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>
/// What a document's front matter says about itself: the fields Word keeps about a file.
///
/// Here rather than in the Word exporter, where it began, because the PDF needs the same
/// answer: its title is the front matter's title too, and two readers of one block would be
/// two opinions about which keys count.
/// </summary>
public sealed record FrontMatter
{
    private static readonly MarkdownPipeline FrontMatterOnly =
        new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();
    public string? Title { get; init; }

    public string? Author { get; init; }

    public string? Subject { get; init; }

    public string? Keywords { get; init; }

    public string? Date { get; init; }

    /// <summary>Not a Word property - there is nowhere to put it - but the title page has.</summary>
    public string? Version { get; init; }

    public static FrontMatter None { get; } = new();

    /// <summary>
    /// The date a cover page shows: the front matter's, or today's.
    ///
    /// Written out rather than left as a placeholder because a cover page almost always wants
    /// a date and this one is true at the moment of export. Not a Word DATE field, which would
    /// keep updating: a report that is filed and read a year later should say when it was
    /// written, not claim to be current. Here rather than in either exporter, so the Word cover
    /// and the PDF cover cannot disagree about which day it is or how to write it.
    /// </summary>
    public string CoverDate() =>
        Date is { Length: > 0 } stated
            ? stated
            : DateTime.Now.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Reads the keys from markdown text, for a caller that has no parsed document to hand.
    /// Front matter is only ever at the very top, so nothing past the closing fence matters.
    /// </summary>
    public static FrontMatter Read(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        return markdown.StartsWith("---", StringComparison.Ordinal)
            || markdown.StartsWith("﻿---", StringComparison.Ordinal)
                ? Read(Markdig.Markdown.Parse(markdown, FrontMatterOnly))
                : None;
    }

    /// <summary>
    /// Reads the handful of keys Word has somewhere to put.
    ///
    /// Deliberately not a YAML parser. Marqora has no YAML library and does not want one for
    /// this: front matter is metadata the preview already declines to render, and the five
    /// fields below are scalars on their own line in every document that has any. Anything
    /// more elaborate - a nested map, a list, a folded block - is passed over rather than
    /// guessed at, which is the right answer for something whose only job is to fill in a
    /// file's properties.
    /// </summary>
    public static FrontMatter Read(MarkdigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        YamlFrontMatterBlock? block = document
            .Descendants<YamlFrontMatterBlock>()
            .FirstOrDefault();

        if (block is null)
        {
            return None;
        }

        string? title = null;
        string? author = null;
        string? subject = null;
        string? keywords = null;
        string? date = null;
        string? version = null;

        for (int i = 0; i < block.Lines.Count; i++)
        {
            string line = block.Lines.Lines[i].Slice.ToString();

            // The fences themselves are part of the block, and a nested key is indented -
            // which is how a value belonging to something else is told apart from a top-level
            // one without understanding the structure it belongs to.
            if (line.Length == 0 || line.StartsWith("---", StringComparison.Ordinal)
                || char.IsWhiteSpace(line[0]))
            {
                continue;
            }

            int colon = line.IndexOf(':', StringComparison.Ordinal);

            if (colon <= 0)
            {
                continue;
            }

            string key = line[..colon].Trim();
            string value = Unquote(line[(colon + 1)..].Trim());

            if (value.Length == 0 || value.StartsWith('[') || value.StartsWith('{'))
            {
                continue;
            }

            switch (key.ToLowerInvariant())
            {
                case "title": title ??= value; break;
                case "author": author ??= value; break;
                case "subject": subject ??= value; break;
                case "description": subject ??= value; break;
                case "keywords": keywords ??= value; break;
                case "tags": keywords ??= value; break;
                case "date": date ??= value; break;
                case "version": version ??= value; break;
                case "revision": version ??= value; break;
                default: break;
            }
        }

        return new FrontMatter
        {
            Title = title,
            Author = author,
            Subject = subject,
            Keywords = keywords,
            Date = date,
            Version = version,
        };
    }

    private static string Unquote(string value) =>
        value.Length >= 2
        && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
            ? value[1..^1]
            : value;
}
