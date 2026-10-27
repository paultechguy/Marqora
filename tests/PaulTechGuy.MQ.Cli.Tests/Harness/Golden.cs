// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Shouldly;

namespace PaulTechGuy.MQ.Cli.Tests.Harness;

/// <summary>
/// Output compared with a file recorded in Golden/, beside this project's source.
///
/// Recorded, never edited by hand: set MQ_UPDATE_GOLDEN=1 in the environment of a test run to
/// write what the code produces now, then read the diff before committing it. A golden file that
/// does not exist fails the test with that instruction, rather than being created quietly.
/// </summary>
internal static class Golden
{
    private static string Folder => Repo.At("tests", "PaulTechGuy.MQ.Cli.Tests", "Golden");

    private static bool Updating => Environment.GetEnvironmentVariable("MQ_UPDATE_GOLDEN") == "1";

    public static void Matches(string name, string actual)
    {
        string path = Path.Combine(Folder, name);

        // Recorded with LF whatever produced it, so a checkout's line-ending setting cannot fail it.
        string normalized = actual.ReplaceLineEndings("\n");

        if (Updating)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, normalized);
            return;
        }

        File.Exists(path).ShouldBeTrue($"No golden file {name}. Run the tests with MQ_UPDATE_GOLDEN=1 to record it, then read it.");

        normalized.ShouldBe(File.ReadAllText(path).ReplaceLineEndings("\n"), $"Golden/{name} differs. If the change is intended, record it with MQ_UPDATE_GOLDEN=1.");
    }
}
