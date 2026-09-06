// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Abstractions.Analysis;

/// <summary>
/// Looks a document over and reports what is wrong with it, without changing anything.
///
/// Implementations must be pure apart from asking the filesystem whether a file exists,
/// which is the whole point of the link checks. Nothing here renders, parses or rewrites.
/// </summary>
public interface IMarkdownAnalyzer
{
    /// <summary>
    /// Every problem found, in no particular order, in the two shapes the editor draws them:
    /// style rules as markers, dead links as decorations with a menu behind them. Both lists
    /// empty means the document is clean, which is the common case and must stay cheap.
    /// </summary>
    AnalysisResult Analyze(AnalysisRequest request);
}
