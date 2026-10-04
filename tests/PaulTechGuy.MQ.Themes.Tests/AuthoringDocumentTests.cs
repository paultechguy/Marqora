// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Themes.Tests;

/// <summary>
/// The two parts of docs/ColorThemes-Authoring.md that are written from the slot list rather
/// than by hand: the slot table, and the JSON skeleton the prompt hands an AI.
///
/// A test rather than a script, because it can read <see cref="ThemeSlots.All"/> as the compiler
/// sees it - a script would have to parse ThemeSlots.cs, callout helper and all, which is a
/// second copy of the list by another name. Run normally it fails when the document has fallen
/// behind; with MARQORA_WRITE_THEME_DOCS=1 it rewrites the two blocks instead, which is what
/// build/Update-ThemeSlotTable.ps1 does.
/// </summary>
public sealed class AuthoringDocumentTests
{
    private const string WriteVariable = "MARQORA_WRITE_THEME_DOCS";

    private static string DocumentPath => Path.Combine(Repository.Root(), "docs", "ColorThemes-Authoring.md");

    [Fact]
    public void The_slot_table_matches_the_slot_list() => Agree("slot-table", SlotTable());

    [Fact]
    public void The_skeleton_matches_the_slot_list() => Agree("skeleton", Skeleton());

    private static void Agree(string block, string expected)
    {
        string path = DocumentPath;
        string text = File.ReadAllText(path).ReplaceLineEndings("\n");
        string start = $"<!-- {block}:start -->\n";
        string end = $"<!-- {block}:end -->";

        int from = text.IndexOf(start, StringComparison.Ordinal);
        int to = text.IndexOf(end, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, $"{Path.GetFileName(path)} has no {block} block.");

        string current = text[(from + start.Length)..to];

        if (current == expected)
        {
            return;
        }

        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
        {
            File.WriteAllText(path, string.Concat(text[..(from + start.Length)], expected, text[to..]), new UTF8Encoding(false));
            return;
        }

        current.ShouldBe(expected, $"The {block} block is out of date. Run: pwsh ./build/Update-ThemeSlotTable.ps1");
    }

    private static string SlotTable()
    {
        StringBuilder table = new();

        table.Append("| Slot | Group | What it colors | Read against | At least |\n");
        table.Append("|---|---|---|---|---|\n");

        foreach (ThemeSlot slot in ThemeSlots.All)
        {
            string against = slot.Contrast?.Against switch
            {
                null => "—",
                ThemeSlots.Page => "the page",
                ThemeSlots.BodyText => "body text, on this fill",
                string other => $"`{other}`",
            };

            string minimum = slot.Contrast is { } rule
                ? rule.Minimum.ToString("0.0", CultureInfo.InvariantCulture) + ":1"
                : "—";

            table.Append(CultureInfo.InvariantCulture, $"| `{slot.Id}` | {slot.Group} | {slot.Meaning} | {against} | {minimum} |\n");
        }

        return table.ToString();
    }

    private static string Skeleton()
    {
        StringBuilder json = new();

        json.Append("```json\n");
        json.Append("{\n");
        json.Append("  \"schema\": 1,\n");
        json.Append("  \"id\": \"your-theme-id\",\n");
        json.Append("  \"name\": \"Your Theme\",\n");
        json.Append("  \"description\": \"One sentence, shown as the theme's tooltip.\",\n");

        foreach (string mode in new[] { "light", "dark" })
        {
            json.Append(CultureInfo.InvariantCulture, $"  \"{mode}\": {{\n");

            for (int i = 0; i < ThemeSlots.All.Count; i++)
            {
                string comma = i < ThemeSlots.All.Count - 1 ? "," : string.Empty;

                json.Append(CultureInfo.InvariantCulture, $"    \"{ThemeSlots.All[i].Id}\": \"#rrggbb\"{comma}\n");
            }

            json.Append(mode == "light" ? "  },\n" : "  }\n");
        }

        json.Append("}\n");
        json.Append("```\n");

        return json.ToString();
    }
}
