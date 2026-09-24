// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Formatting.Tests;

/// <summary>
/// Table padding is measured in editor columns, not UTF-16 code units.
///
/// The case that prompted these is a status column of ✅ and ❌. Each is one code unit and two
/// columns on screen, so padding by <see cref="string.Length"/> left every emoji row one column
/// past the header and the divider.
/// </summary>
public sealed class TableWidthTests
{
    private static readonly FormatOptions TablesOnly = new()
    {
        HeadingSpace = false,
        TrailingWhitespace = false,
        NormalizeMarkers = false,
        LineEndings = false,
        BlankLines = false,
        ListMarkerSpace = false,
        LinkSyntax = false,
        EofNewline = false,
        CollapseBlanks = false,
        OrderedNumbering = false,
        BlockquoteSpace = false,
        FormatTables = true,
        TidyCodeFences = false,
        SetextToAtx = false,
        UnifyEmphasis = false,
        ReflowParagraphs = false,
    };

    private static string Format(string markdown) =>
        new MarkdownFormatter().Format(markdown, TablesOnly).Text.Replace("\r\n", "\n");

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    // The body rows were already right; the header and divider were one column short.
    private static readonly string StatusTable = Lines(
        "| Workload                                                       | Status                                        |",
        "| -------------------------------------------------------------- | --------------------------------------------- |",
        "| **CI/CD for the Exeter LMS web application**                   | ✅ Replaced by **GitHub Actions**             |",
        "| **~5 tools** for curriculum changes + activity reporting       | ✅ Migrated to the **Hangfire** job scheduler |",
        "| **Certificate tooling** (DigiCert → PFX for Azure App Service) | ❌ **Still on the VM**                        |",
        "| **Task tooling carries secrets**                               | ✅ Replaced by **Azure Vault**                |");

    [Fact]
    public void Emoji_rows_line_up_with_the_header_and_divider()
    {
        string counted = Lines(
            "| Workload | Status |",
            "| --- | --- |",
            "| **CI/CD for the Exeter LMS web application** | ✅ Replaced by **GitHub Actions** |",
            "| **~5 tools** for curriculum changes + activity reporting | ✅ Migrated to the **Hangfire** job scheduler |",
            "| **Certificate tooling** (DigiCert → PFX for Azure App Service) | ❌ **Still on the VM** |",
            "| **Task tooling carries secrets** | ✅ Replaced by **Azure Vault** |");

        Format(counted).ShouldBe(StatusTable);
    }

    [Fact]
    public void An_aligned_emoji_table_is_left_alone()
    {
        Format(StatusTable).ShouldBe(StatusTable);
    }

    [Fact]
    public void A_joined_emoji_counts_as_one_wide_glyph()
    {
        // A family is five code points joined by ZWJ, eleven code units, and one two-column
        // glyph; the flag is two regional indicators drawn as one.
        string table = Lines(
            "| Who | Where |",
            "| --- | --- |",
            "| 👨‍👩‍👧 | 🇺🇸 |",
            "| abc | de |");

        Format(table).ShouldBe(Lines(
            "| Who | Where |",
            "| --- | ----- |",
            "| 👨‍👩‍👧  | 🇺🇸    |",
            "| abc | de    |"));
    }
}
