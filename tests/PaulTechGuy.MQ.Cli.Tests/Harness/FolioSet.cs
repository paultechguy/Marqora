// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace PaulTechGuy.MQ.Cli.Tests.Harness;

/// <summary>
/// A small set to share, with one of everything the Folio report knows: a picture that travels, a
/// missing one, a web one and a link to a document outside the set. guide.md and setup.md, in a
/// workspace of their own.
/// </summary>
internal static class FolioSet
{
    /// <summary>A real 1 x 1 PNG, so the planner can read its width.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public const string Guide = """
        # Guide

        ![Logo](images/logo.png)

        ![Gone](images/gone.png)

        ![Web](https://pictures.example.com/pic.png)

        [Elsewhere](other.md)

        """;

    public const string Setup = """
        # Setup

        ![Missing](nothing.png)

        """;

    public static void WriteTo(Workspace workspace)
    {
        workspace.Text("docs/guide.md", Guide.ReplaceLineEndings("\n"));
        workspace.Text("docs/setup.md", Setup.ReplaceLineEndings("\n"));
        workspace.Bytes("docs/images/logo.png", Png);
    }

    /// <summary>Each JSON line of a run's stdout, parsed.</summary>
    public static JsonElement[] JsonLines(CliResult result) =>
        [.. result.Out
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())];
}
