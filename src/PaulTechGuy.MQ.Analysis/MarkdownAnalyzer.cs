// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Abstractions.Analysis;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Analysis;

/// <summary>
/// Runs every check over one document and collects what they find.
///
/// The two families are deliberately separate. Link checks work from the parsed link list
/// the renderer produced, so they see what the reader will see. Style checks work from the
/// raw lines, because whitespace and marker spacing are gone by the time anything is parsed.
/// </summary>
public sealed class MarkdownAnalyzer : IMarkdownAnalyzer
{
    public IReadOnlyList<Diagnostic> Analyze(AnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Text.Length == 0)
        {
            return [];
        }

        List<Diagnostic> found = [];

        LinkChecks.Run(request, found);

        string[] lines = request.Text.Split('\n');

        // Split on \n alone, so a CRLF file leaves a stray \r at the end of every line. It
        // would be reported as trailing whitespace on every single line, which is noise
        // rather than a finding.
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd('\r');
        }

        StyleChecks.Run(lines, MarkdownRegionScanner.FindProtectedLines(lines), found);

        return found;
    }
}
