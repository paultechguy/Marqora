// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli.Verbs;

/// <summary>
/// A verb whose command line exists but whose work has not landed yet. Says so and exits 2, the
/// way a usage error does: the command line asked for something this build cannot do.
/// </summary>
internal static class PendingVerb
{
    public static Task<ExitCode> RunAsync(RunContext context, string verb)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Reporter.Error(null, $"'{verb}' is not available in this build yet.");
        return Task.FromResult(ExitCode.Usage);
    }
}
