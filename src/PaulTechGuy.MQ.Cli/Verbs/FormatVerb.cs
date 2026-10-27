// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using PaulTechGuy.MQ.Abstractions.Formatting;
using PaulTechGuy.MQ.Cli.Commands;
using PaulTechGuy.MQ.Cli.Settings;
using PaulTechGuy.MQ.Cli.Text;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Formatting;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// format: Format Document (CLI-VB-FMT-01). The app's own formatter with the formatting rules in
/// force - the built-in ones, or the Preferences --settings names.
///
/// The one text verb that decides line endings and the final newline itself, through the rules'
/// own options (amendment A7); every other verb leaves them as the file had them.
/// </summary>
internal static class FormatVerb
{
    public static async Task<ExitCode> RunAsync(RunContext context, ParseResult parse, InputOptions inputs, ModeOptions modes)
    {
        ArgumentNullException.ThrowIfNull(context);

        FormatOptions options = (await SettingsSource.LoadAsync(context).ConfigureAwait(false)).Settings.Formatting;
        var formatter = new MarkdownFormatter();

        return await TextVerbRunner.RunAsync(
            context,
            parse,
            inputs,
            modes,
            document =>
            {
                FormattedMarkdown formatted = formatter.Format(document.File.Text, options);

                return new VerbResult(
                    formatted.Text,
                    [],
                    formatted.ChangedLines == 1 ? "1 line changed" : $"{formatted.ChangedLines} lines changed");
            },
            verbOwnsEndings: true).ConfigureAwait(false);
    }
}
