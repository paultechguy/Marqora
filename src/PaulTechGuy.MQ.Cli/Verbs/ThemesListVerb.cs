// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Encodings.Web;
using System.Text.Json;
using PaulTechGuy.MQ.Themes;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// themes list (CLI-TH-05): every color theme's id, name and description, in the gallery's order -
/// Default first, then by name - read from the same catalog the gallery and the exports use, so it
/// can never offer a theme an export would refuse. A theme file added to the app is a row here with
/// no change to the CLI.
///
/// With --json, one array of objects rather than a line per theme: the list is one answer, not one
/// per input.
/// </summary>
internal static class ThemesListVerb
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static Task<ExitCode> RunAsync(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<ColorTheme> themes = context.Environment.Themes().Themes;

        if (context.Results.IsJson)
        {
            context.Results.Raw(JsonSerializer.Serialize(
                themes.Select(theme => new { id = theme.Id, name = theme.Name, description = theme.Description }),
                Options));

            return Task.FromResult(ExitCode.Success);
        }

        int idWidth = themes.Max(theme => theme.Id.Length);
        int nameWidth = themes.Max(theme => theme.Name.Length);

        foreach (ColorTheme theme in themes)
        {
            context.Results.Line($"{theme.Id.PadRight(idWidth)}  {theme.Name.PadRight(nameWidth)}  {theme.Description}".TrimEnd());
        }

        return Task.FromResult(ExitCode.Success);
    }
}
