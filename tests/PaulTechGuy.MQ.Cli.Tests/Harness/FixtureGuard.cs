// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Xunit;

[assembly: AssemblyFixture(typeof(FixtureGuard))]

namespace PaulTechGuy.MQ.Cli.Tests.Harness;

/// <summary>
/// The markdown fixtures in the repository, hashed before the first test and again after the
/// last: a run that changed one fails, naming it (CLI-TST-03). UltimateMarkdownContent.md's
/// "mistakes" are its tests, and the baseline's inputs must stay what the baseline was made from,
/// so a CLI test that wrote over one would be breaking two other things at once. The tour's two
/// documents are what the manual's examples are run against, and a copy is what they change.
/// </summary>
public sealed class FixtureGuard : IDisposable
{
    private static readonly string[] Fixtures =
    [
        "docs/UltimateMarkdownContent.md",
        "webshell/cheatsheet.md",
        "tests/baselines/cli/inputs/pictures.md",
        "docs/cli-tour/Guide.md",
        "docs/cli-tour/Setup.md",
    ];

    private readonly Dictionary<string, string> _before = Fixtures.ToDictionary(path => path, Hash);

    public void Dispose()
    {
        string[] changed = [.. Fixtures.Where(path => Hash(path) != _before[path])];

        if (changed.Length > 0)
        {
            throw new InvalidOperationException(
                "A test run changed a markdown fixture, which tests may only read: " + string.Join(", ", changed));
        }
    }

    private static string Hash(string relative) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Repo.At(relative.Split('/')))));
}
