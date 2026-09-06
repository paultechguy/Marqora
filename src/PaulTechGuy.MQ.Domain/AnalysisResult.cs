// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// Everything one pass over a document found, in the two shapes the editor draws.
///
/// The split is not tidiness. <see cref="Diagnostics"/> become Monaco markers, which is right
/// for the style rules: they are advisory, the formatter fixes all of them on request, and
/// nothing is offered per occurrence. <see cref="LinkFindings"/> become decorations with a menu
/// behind them, because a dead link has a specific repair that only makes sense for that one
/// link - which heading was meant, which file was meant.
///
/// One call rather than two so a caller pays for one walk over the document and one hop off the
/// UI thread.
/// </summary>
public sealed record AnalysisResult
{
    public static AnalysisResult Empty { get; } = new();

    /// <summary>Style rules, drawn as markers.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = [];

    /// <summary>Links that lead nowhere, drawn as decorations.</summary>
    public IReadOnlyList<LinkFinding> LinkFindings { get; init; } = [];
}
