// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Cli.Outputs;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Folio;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// unpack: File > Open Folio, without the window (CLI-VB-UNP-01, amendments A4 and A5c).
///
/// A single-file Folio only, as Open Folio reads only that form: a folder Folio is already its files
/// and a zip one is an ordinary zip, and each is refused with what to do instead. The target must
/// be missing or empty, as the app's own unpacker asks, unless --overwrite is given: then what is in
/// it is replaced, the Folio unpacked beside it first, and some folders are never emptied at all
/// (see FolderReplacement).
/// </summary>
internal static class UnpackVerb
{
    public static async Task<ExitCode> RunAsync(RunContext context, ParseResult parse, Argument<string> folio, Option<string> outDir, Option<bool> overwrite)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parse);

        string given = parse.GetValue(folio)!;
        string source = context.FullPath(given);
        string target = context.FullPath(parse.GetValue(outDir)!);

        if (Directory.Exists(source))
        {
            return Fail(context, given, ExitCode.Input, "is a folder. A Folio in the folder form is already its documents and pictures; open them where they are.");
        }

        if (string.Equals(Path.GetExtension(source), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(context, given, ExitCode.Input, "is a zip. A Folio in the zip form is its files, compressed; extract it with any zip tool.");
        }

        if (!File.Exists(source))
        {
            return Fail(context, given, ExitCode.Input, "No such file.");
        }

        string html;

        try
        {
            html = await File.ReadAllTextAsync(source, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(context, given, ExitCode.Input, $"Could not be read: {ex.Message}");
        }

        if (FolioPayload.TryDecode(html) is not { } payload)
        {
            return Fail(context, given, ExitCode.Input, "is not a Folio that carries its documents, so there is nothing to take out of it. It can still be read in a browser.");
        }

        bool replacing = Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any();

        if (replacing && !parse.GetValue(overwrite))
        {
            return Fail(context, given, ExitCode.Output, $"{target} already has something in it. Pass --overwrite to replace its contents.");
        }

        if (replacing && FolderReplacement.Refusal(target, [source], context.Environment.CurrentDirectory) is { } refusal)
        {
            return Fail(context, given, ExitCode.Output, $"{target}: {refusal}");
        }

        FolioUnpackResult result;

        try
        {
            if (replacing)
            {
                FolioUnpackResult? unpacked = null;
                string? left = await FolderReplacement.ReplaceAsync(target, overwrite: true, pending =>
                {
                    unpacked = FolioUnpacker.Unpack(html, payload, pending);

                    // Named where they end up, not where they were written.
                    unpacked = unpacked with { Documents = [.. unpacked.Documents.Select(d => Path.Combine(target, Path.GetRelativePath(pending, d)))] };

                    return Task.CompletedTask;
                }).ConfigureAwait(false);

                result = unpacked!;

                if (left is not null)
                {
                    context.Reporter.Warning(given, $"The Folio is unpacked, but what it replaced could not all be deleted; it was left in {left}.");
                }
            }
            else
            {
                result = FolioUnpacker.Unpack(html, payload, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(context, given, ExitCode.Output, $"Could not unpack into {target}: {ex.Message}");
        }

        foreach (string refused in result.Refused)
        {
            context.Reporter.Warning(given, refused);
        }

        string documents = result.Documents.Count == 1 ? "1 document" : $"{result.Documents.Count} documents";
        string images = result.Images == 1 ? "1 picture" : $"{result.Images} pictures";

        context.Reporter.Summary(given, $"unpacked {documents} and {images} into {target}.");
        context.Results.Json(new JsonLine
        {
            Path = given,
            Verb = context.Verb,
            Outcome = result.Refused.Count > 0 ? Outcomes.Failed : Outcomes.Exported,
            Outputs = result.Documents,
            Message = result.Refused.Count > 0 ? string.Join(" ", result.Refused) : null,
        });

        // What was refused is something in the Folio that was unsafe or unwritable: the input's fault.
        return result.Refused.Count > 0 ? ExitCode.Input : ExitCode.Success;
    }

    private static ExitCode Fail(RunContext context, string given, ExitCode code, string message)
    {
        context.Reporter.Error(given, message);
        context.Results.Json(new JsonLine { Path = given, Verb = context.Verb, Outcome = Outcomes.Failed, Message = message });

        return code;
    }
}
