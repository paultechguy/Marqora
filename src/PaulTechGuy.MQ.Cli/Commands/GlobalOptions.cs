// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;

namespace PaulTechGuy.MQ.Cli.Commands;

/// <summary>
/// The options every verb takes, declared once on the root and inherited by each command beneath
/// it, so they can be written before or after the verb and mean the same thing either way.
/// </summary>
internal sealed class GlobalOptions
{
    public Option<bool> Json { get; } = new("--json")
    {
        Description = "Write one JSON object per input to stdout instead of text. Messages stay on stderr.",
        Recursive = true,
    };

    public Option<bool> Quiet { get; } = new("--quiet", "-q")
    {
        Description = "Say nothing on stderr but errors and warnings.",
        Recursive = true,
    };

    public Option<bool> Verbose { get; } = new("--verbose", "-v")
    {
        Description = "Also say the settings and theme in force, where they came from, and how long things took.",
        Recursive = true,
    };

    public Option<string> Color { get; } = new Option<string>("--color")
    {
        Description = "Color messages on stderr: auto (only on a terminal, unless NO_COLOR is set), always, or never.",
        DefaultValueFactory = _ => "auto",
        HelpName = "auto|always|never",
        Recursive = true,
    }.AcceptOnlyFromAmong("auto", "always", "never");

    public Option<bool> FailFast { get; } = new("--fail-fast")
    {
        Description = "Stop at the first file that fails, instead of carrying on with the rest.",
        Recursive = true,
    };

    public Option<string?> Settings { get; } = new("--settings")
    {
        Description = "Use the desktop app's saved Preferences (app), or an exported Preferences file. Without it, built-in defaults.",
        HelpName = "app|file",
        Recursive = true,
    };

    public IEnumerable<Option> All => [Json, Quiet, Verbose, Color, FailFast, Settings];
}
