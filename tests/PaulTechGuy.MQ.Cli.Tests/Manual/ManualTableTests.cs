// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Manual;

/// <summary>CLI-DOC-01 and CLI-RPT-01: the manual's exit-code table is ExitCode, number for number.</summary>
public sealed class ManualTableTests
{
    [Fact]
    public void The_exit_code_table_lists_exactly_the_codes_there_are()
    {
        string section = Manual.Cli.Section("## Exit codes").ShouldNotBeNull();

        int[] documented = [.. section.Split('\n')
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)[1])
            .Where(cell => cell.Length > 0 && cell.All(char.IsAsciiDigit))
            .Select(cell => int.Parse(cell, CultureInfo.InvariantCulture))];

        documented.ShouldBe([.. Enum.GetValues<ExitCode>().Select(code => (int)code)]);
    }
}
