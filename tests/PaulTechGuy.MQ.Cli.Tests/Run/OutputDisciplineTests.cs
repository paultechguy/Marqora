// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Run;

/// <summary>
/// CLI-RPT-02: every message goes through the reporter and every result through the result
/// writer, so stdout never receives a message and the list of things the CLI can say is the list
/// of calls into one class. Checked in the source, where a stray Console.WriteLine would hide.
/// </summary>
public sealed partial class OutputDisciplineTests
{
    /// <summary>
    /// The files allowed to touch the console: the two writers, the environment that opens the
    /// streams, and the M0 spike with its logger, which go when export pdf replaces them.
    /// </summary>
    private static readonly string[] Allowed =
    [
        "Reporting/Reporter.cs",
        "Reporting/ResultWriter.cs",
        "CliEnvironment.cs",
        "Spike.cs",
        "StderrLogger.cs",
    ];

    [Fact]
    public void Only_the_writers_touch_the_console()
    {
        string root = Repo.At("src", "PaulTechGuy.MQ.Cli");

        string[] offenders = [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !Allowed.Contains(Path.GetRelativePath(root, path).Replace('\\', '/')))
            .Where(path => ConsoleUse().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))];

        offenders.ShouldBeEmpty();
    }

    [GeneratedRegex(@"\bConsole\.(Out|Error|Write|WriteLine)\b")]
    private static partial Regex ConsoleUse();
}
