// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Inputs;
using PaulTechGuy.MQ.Cli.Reporting;

namespace PaulTechGuy.MQ.Cli.Outputs;

/// <summary>What an output is: one file, or a folder that is filled.</summary>
internal enum OutputKind
{
    File,

    Folder,
}

/// <summary>One output a run means to write, and the input (or set) it is reported against.</summary>
internal sealed record PlannedOutput(string ReportedAs, string FullPath, OutputKind Kind);

/// <summary>
/// The checks every output passes before any work starts (CLI-OUT-07, CLI-OUT-08): nothing is
/// planned, rendered or fetched for a run that could not write its result.
///
/// In order:
/// <list type="number">
/// <item>Two outputs at the same path are an input error, exit 3: the inputs collide, and no flag
/// can fix that.</item>
/// <item>An output that is one of the inputs is refused, exit 4, even with --overwrite: replacing
/// the document being shared with what was made from it is never what was meant.</item>
/// <item>A file that exists is refused without --overwrite, exit 4.</item>
/// <item>A folder with anything in it is refused without --overwrite, exit 4. With it, the folder's
/// contents are replaced, unless it is one <see cref="FolderReplacement"/> never empties
/// (amendment A5c).</item>
/// </list>
/// Every problem is reported, not only the first, so one run shows everything that needs fixing.
/// </summary>
internal static class OutputPlan
{
    public static ExitCode Check(RunContext context, IReadOnlyList<InputFile> inputs, IReadOnlyList<PlannedOutput> outputs, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);

        ExitCode code = ExitCode.Success;

        foreach (IGrouping<string, PlannedOutput> clash in outputs
            .GroupBy(o => Path.TrimEndingDirectorySeparator(o.FullPath), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            string names = string.Join(" and ", clash.Select(o => o.ReportedAs));

            code = ExitCodes.Worst(code, Refuse(context, clash.First().ReportedAs, ExitCode.Input, $"{names} would both be written to {clash.Key}. Rename one, or share them separately."));
        }

        HashSet<string> sources = new(
            inputs.Where(i => !i.IsStandardInput).Select(i => i.FullPath!),
            StringComparer.OrdinalIgnoreCase);

        foreach (PlannedOutput output in outputs)
        {
            string path = Path.TrimEndingDirectorySeparator(output.FullPath);

            if (sources.Contains(path))
            {
                code = ExitCodes.Worst(code, Refuse(context, output.ReportedAs, ExitCode.Output, "It is one of the inputs, and an input is never written over, even with --overwrite."));
            }
            else if (output.Kind == OutputKind.Folder && File.Exists(path))
            {
                code = ExitCodes.Worst(code, Refuse(context, output.ReportedAs, ExitCode.Output, "It is a file, not a folder."));
            }
            else if (output.Kind == OutputKind.Folder && Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
            {
                string? refusal = overwrite
                    ? FolderReplacement.Refusal(path, sources, context.Environment.CurrentDirectory)
                    : "It already has something in it. Pass --overwrite to replace its contents.";

                if (refusal is not null)
                {
                    code = ExitCodes.Worst(code, Refuse(context, output.ReportedAs, ExitCode.Output, refusal));
                }
            }
            else if (output.Kind == OutputKind.File && Directory.Exists(path))
            {
                code = ExitCodes.Worst(code, Refuse(context, output.ReportedAs, ExitCode.Output, "It is a folder, not a file."));
            }
            else if (output.Kind == OutputKind.File && File.Exists(path) && !overwrite)
            {
                // Named, because an export reports against its input, and "it" would be the document.
                code = ExitCodes.Worst(code, Refuse(context, output.ReportedAs, ExitCode.Output, $"{path} already exists. Pass --overwrite to replace it."));
            }
        }

        return code;
    }

    private static ExitCode Refuse(RunContext context, string reportedAs, ExitCode code, string message)
    {
        context.Reporter.Error(reportedAs, message);
        context.Results.Json(new JsonLine { Path = reportedAs, Verb = context.Verb, Outcome = Outcomes.Failed, Message = message });

        return code;
    }
}
