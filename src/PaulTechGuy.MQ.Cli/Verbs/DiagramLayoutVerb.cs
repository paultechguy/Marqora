// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Cli.Text;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// diagram-layout: Diagram Layout on every mermaid diagram, or the n-th (CLI-VB-DGL-01).
///
/// Each diagram's edit is the app's own, from <see cref="DiagramLayout.EditFor"/>. A diagram whose
/// type has no direction - a sequence diagram, a Gantt chart, a pie - is left alone and named, which
/// is the case <see cref="DiagramLayout.Current"/> answers null for; one already running the asked
/// way needs nothing and is not mentioned.
/// </summary>
internal static class DiagramLayoutVerb
{
    public static Task<ExitCode> RunAsync(
        RunContext context,
        ParseResult parse,
        InputOptions inputs,
        ModeOptions modes,
        Option<string> direction,
        Option<int?> diagram)
    {
        ArgumentNullException.ThrowIfNull(parse);

        DiagramDirection want = parse.GetValue(direction) == "left-to-right" ? DiagramDirection.LeftToRight : DiagramDirection.TopToBottom;
        int? only = parse.GetValue(diagram);

        return TextVerbRunner.RunAsync(context, parse, inputs, modes, document =>
        {
            List<string> lines = [.. document.File.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')];
            IReadOnlyList<(int Open, int Close)> fences = DiagramLayout.Fences(lines);
            var findings = new List<DocumentFinding>();

            if (only is { } n && n > fences.Count)
            {
                findings.Add(new DocumentFinding(0, 0, "no-such-diagram", FindingSeverity.Warning,
                    fences.Count == 1 ? $"There is no diagram {n}: the document has 1." : $"There is no diagram {n}: the document has {fences.Count}."));

                return new VerbResult(document.File.Text, findings);
            }

            int changed = 0;

            // From the last diagram to the first: a diagram that gains a line moves only those after it.
            for (int index = fences.Count - 1; index >= 0; index--)
            {
                if (only is { } wanted && index != wanted - 1)
                {
                    continue;
                }

                (int open, int close) = fences[index];

                if (DiagramLayout.Current(lines, open, close) is null)
                {
                    findings.Add(new DocumentFinding(open + 1, 0, "no-direction", FindingSeverity.Info,
                        $"Diagram {index + 1} is a kind with no direction to set, so it was left alone."));
                    continue;
                }

                if (DiagramLayout.EditFor(lines, open, close, want) is not { } edit)
                {
                    continue;
                }

                if (edit.InsertsLine)
                {
                    lines.Insert(edit.Line + 1, edit.Text);
                }
                else
                {
                    string line = lines[edit.Line];
                    lines[edit.Line] = line[..edit.Start] + edit.Text + line[edit.End..];
                }

                changed++;
            }

            findings.Reverse();

            return new VerbResult(string.Join('\n', lines), findings, changed == 1 ? "1 diagram turned" : $"{changed} diagrams turned");
        });
    }
}
