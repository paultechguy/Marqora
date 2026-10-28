// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli.Tests.Harness;

/// <summary>Where things are in the repository the tests were built from.</summary>
internal static class Repo
{
    /// <summary>The repository root: the folder holding the solution, found by walking up from the test's output.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>The configuration this assembly was built in, which is the one the CLI beside it was built in.</summary>
    public static string Configuration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    /// <summary>The built marqora-cli.exe, for the tests that need a real process.</summary>
    public static string CliExecutable => Path.Combine(
        Root, "src", "PaulTechGuy.MQ.Cli", "bin", Configuration, "net10.0-windows10.0.19041.0", "win-x64", "marqora-cli.exe");

    /// <summary>The folder the built marqora-cli.exe is in, with the web shell beside it.</summary>
    public static string CliFolder => Path.GetDirectoryName(CliExecutable)!;

    /// <summary>A path inside the repository.</summary>
    public static string At(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string FindRoot()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "PaulTechGuy.MQ.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("The tests are not running inside the Marqora repository.");
    }
}
