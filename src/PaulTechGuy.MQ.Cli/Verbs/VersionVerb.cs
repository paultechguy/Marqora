// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.CommandLine;
using System.CommandLine.Invocation;
using PaulTechGuy.MQ.Cli.Reporting;
using PaulTechGuy.MQ.Services;

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// marqora-cli's version (CLI-VB-VER-01), read by <see cref="ProductVersion"/> exactly as the app
/// reads its own. Both executables take their version from the same Directory.Build.props, so this
/// is the app's version from the same build (amendment A11).
/// </summary>
internal static class VersionVerb
{
    public static string Current { get; } = ProductVersion.Of(typeof(VersionVerb).Assembly);

    public static Task<ExitCode> RunAsync(RunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Results.Line(Current);
        context.Results.Json(new JsonLine { Path = string.Empty, Verb = context.Verb, Outcome = Outcomes.Passed, Message = Current });

        return Task.FromResult(ExitCode.Success);
    }

    /// <summary>--version, answering exactly as the version verb does.</summary>
    internal sealed class Action(CliEnvironment environment) : SynchronousCommandLineAction
    {
        public override bool ClearsParseErrors => true;

        public override int Invoke(ParseResult parseResult)
        {
            environment.Out.Write(Current);
            environment.Out.Write('\n');

            return (int)ExitCode.Success;
        }
    }
}
