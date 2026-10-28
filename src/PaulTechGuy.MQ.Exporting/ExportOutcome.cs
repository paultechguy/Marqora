// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Exporting;

/// <summary>
/// What an export job did: the files it wrote and what it could not carry into them.
///
/// The same shape for every job and both callers. The app turns <see cref="Issues"/> into its
/// report window, and marqora-cli into findings (CLI-EX-03); neither has to know which format
/// raised a row. No file in <see cref="Outputs"/> means nothing was written, and
/// <see cref="Rendering"/> says why when the renderer was the reason.
/// </summary>
public sealed record ExportOutcome(IReadOnlyList<string> Outputs, IReadOnlyList<ExportIssue> Issues)
{
    /// <summary>How the render the export needed ended. Rendered for any job that wrote its file.</summary>
    public BridgeResult Rendering { get; init; } = BridgeResult.Rendered;

    /// <summary>The engine that printed a PDF; null for the other formats.</summary>
    public PrintEngine? Engine { get; init; }

    /// <summary>Whether anything was written.</summary>
    public bool Written => Outputs.Count > 0;

    /// <summary>One file, written.</summary>
    public static ExportOutcome Wrote(string path, IReadOnlyList<ExportIssue> issues) => new([path], issues);

    /// <summary>Nothing written, because the render the export needed did not finish.</summary>
    public static ExportOutcome NotRendered(BridgeResult rendering) => new([], []) { Rendering = rendering };
}
