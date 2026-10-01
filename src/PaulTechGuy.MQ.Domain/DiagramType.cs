// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// What a mermaid diagram calls itself: the first word of its definition, "flowchart" or
/// "sequenceDiagram" - the name a review comment on the diagram goes by in its card and in the
/// CriticMarkup note an AI reads.
///
/// Read from the source rather than the preview. By the time a reader points at a diagram,
/// mermaid has replaced its definition with a drawing, and the drawing's own name for itself
/// ("flowchart-v2") is not one the author ever wrote.
/// </summary>
public static class DiagramType
{
    /// <summary>What a diagram is called when its definition does not say.</summary>
    public const string Fallback = "diagram";

    /// <summary>How far past the fence the first word is looked for.</summary>
    private const int MaxLines = 50;

    /// <summary>The diagram's type, from the definition under the fence on <paramref name="fenceLine"/>.</summary>
    /// <param name="fenceLine">Zero-based line of the opening fence, as the preview stamps it.</param>
    public static string At(string source, int fenceLine)
    {
        ArgumentNullException.ThrowIfNull(source);

        string[] lines = source.Split('\n');
        bool inFrontMatter = false;
        bool first = true;

        for (int i = fenceLine + 1; i >= 1 && i < lines.Length && i <= fenceLine + MaxLines; i++)
        {
            string line = lines[i].Trim();

            // A mermaid definition can open with YAML front matter between --- lines, for its
            // title and config, before the line that says what it is.
            if (line == "---" && (first || inFrontMatter))
            {
                inFrontMatter = !inFrontMatter;
                first = false;
                continue;
            }

            first = false;

            if (inFrontMatter || line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                return Fallback;
            }

            int end = 0;

            while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '-'))
            {
                end++;
            }

            return end > 0 ? line[..end] : Fallback;
        }

        return Fallback;
    }
}
