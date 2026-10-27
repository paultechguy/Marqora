// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Repositories;

namespace PaulTechGuy.MQ.Cli.Tests.Harness;

/// <summary>What a run left: its exit code and everything it wrote to each stream.</summary>
internal sealed record CliResult(int ExitCode, string Out, string Error)
{
    /// <summary>stdout as the bytes it was, for the tests that care about a byte order mark or an ending.</summary>
    public byte[] OutBytes { get; init; } = [];

    public override string ToString() => $"exit {ExitCode}\n--- stdout\n{Out}\n--- stderr\n{Error}";
}

/// <summary>
/// Runs marqora-cli. <see cref="RunAsync"/> calls CliApp in this process with streams of the
/// test's own, which is how nearly every test runs: fast, and with nothing shared between tests
/// running at once. <see cref="RunProcessAsync"/> starts the built executable, for what only a
/// real process has.
/// </summary>
internal static class CliRunner
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <param name="errorIsTerminal">Whether the run believes stderr is a terminal, which decides color under --color auto.</param>
    /// <param name="variables">The environment variables the run can see. Only NO_COLOR is ever read.</param>
    public static async Task<CliResult> RunAsync(
        string[] args,
        string? currentDirectory = null,
        byte[]? standardInput = null,
        bool errorIsTerminal = false,
        IReadOnlyDictionary<string, string>? variables = null,
        IAppPaths? appPaths = null,
        CancellationToken cancellationToken = default)
    {
        var output = new MemoryStream();
        var outWriter = new StreamWriter(output, Utf8NoBom) { AutoFlush = true };
        var error = new StringWriter();

        var environment = new CliEnvironment(
            outWriter,
            error,
            () => new MemoryStream(standardInput ?? []),
            errorIsTerminal,
            name => variables is not null && variables.TryGetValue(name, out string? value) ? value : null,
            currentDirectory ?? Repo.Root)
        {
            // Never the Preferences of the machine running the tests: a folder of the test's own, or
            // one that does not exist.
            AppPaths = () => appPaths ?? new AppPaths(Path.Combine(Path.GetTempPath(), "marqora-cli-tests", "no-app"), Repo.Root),
        };

        int code = await CliApp.RunAsync(args, environment, cancellationToken);

        // Not with the run's token: a canceled run still has output to read back.
        await outWriter.FlushAsync(CancellationToken.None);
        byte[] bytes = output.ToArray();

        return new CliResult(code, Utf8NoBom.GetString(bytes), error.ToString()) { OutBytes = bytes };
    }

    /// <summary>The built executable, as a shell would start it, with its own standard handles.</summary>
    public static async Task<CliResult> RunProcessAsync(
        string[] args,
        string? currentDirectory = null,
        byte[]? standardInput = null,
        IReadOnlyDictionary<string, string?>? variables = null,
        CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(Repo.CliExecutable)
        {
            WorkingDirectory = currentDirectory ?? Repo.Root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        // The run must not inherit this machine's own choice about color.
        start.Environment.Remove("NO_COLOR");

        foreach ((string name, string? value) in variables ?? new Dictionary<string, string?>())
        {
            start.Environment[name] = value;
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("marqora-cli did not start.");

        var outCopy = new MemoryStream();
        var errCopy = new MemoryStream();

        Task reading = Task.WhenAll(
            process.StandardOutput.BaseStream.CopyToAsync(outCopy, cancellationToken),
            process.StandardError.BaseStream.CopyToAsync(errCopy, cancellationToken));

        await process.StandardInput.BaseStream.WriteAsync(standardInput ?? [], cancellationToken);
        process.StandardInput.Close();

        await reading;
        await process.WaitForExitAsync(cancellationToken);

        byte[] bytes = outCopy.ToArray();

        return new CliResult(process.ExitCode, Utf8NoBom.GetString(bytes), Utf8NoBom.GetString(errCopy.ToArray())) { OutBytes = bytes };
    }
}
