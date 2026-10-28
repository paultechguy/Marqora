// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Reporting;

namespace PaulTechGuy.MQ.Cli;

/// <summary>What one verb's run has to work with, built from the command line it was given.</summary>
internal sealed class RunContext
{
    public required CliEnvironment Environment { get; init; }

    public required Reporter Reporter { get; init; }

    public required ResultWriter Results { get; init; }

    /// <summary>The verb's canonical name, as the JSON lines and messages give it: "format", "export pdf".</summary>
    public required string Verb { get; init; }

    public required bool FailFast { get; init; }

    /// <summary>--settings as given: null for built-in defaults, "app", or a file path.</summary>
    public required string? Settings { get; init; }

    public required CancellationToken CancellationToken { get; init; }

    /// <summary>A path the user gave, made absolute against the folder the run started in.</summary>
    public string FullPath(string path) => Path.GetFullPath(path, Environment.CurrentDirectory);
}
