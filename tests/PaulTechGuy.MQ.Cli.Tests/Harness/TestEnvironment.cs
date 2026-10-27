// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli.Tests.Harness;

/// <summary>Environments for the tests that build pieces of the CLI rather than running it whole.</summary>
internal static class TestEnvironment
{
    /// <summary>One that writes nowhere and reads nothing, for building a command tree to inspect.</summary>
    public static CliEnvironment Discarding() => new(
        TextWriter.Null,
        TextWriter.Null,
        () => Stream.Null,
        ErrorIsTerminal: false,
        _ => null,
        Repo.Root);
}
