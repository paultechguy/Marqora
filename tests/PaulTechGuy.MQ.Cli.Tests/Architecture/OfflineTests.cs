// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics.Tracing;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Folio;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Architecture;

/// <summary>The offline tests run alone: the listener hears the whole process, so nothing may connect beside them.</summary>
[CollectionDefinition(nameof(OfflineRunsAlone), DisableParallelization = true)]
public sealed class OfflineRunsAlone;

/// <summary>
/// CLI-NF-03: offline by default. Every M1 verb runs in-process under a listener on the runtime's
/// own socket and HTTP telemetry, and not one connection starts - folio's --dry-run with
/// --fetch-web-images included, since it names the sites and goes to none. Only a folio run that
/// actually fetches may connect, and that is the one path not run here.
///
/// The listener is shown to work first, against a loopback connection of the test's own, so a
/// silence later means nothing connected rather than nothing listening. A source scan holds the
/// other half: the only HTTP client in the tree is built inside the fetch method.
/// </summary>
[Collection(nameof(OfflineRunsAlone))]
public sealed partial class OfflineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_listener_hears_a_connection()
    {
        using var listener = new NetworkListener();
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)server.LocalEndpoint).Port, Ct);
        }

        server.Stop();

        listener.Connections.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task No_m1_verb_opens_a_connection()
    {
        using var workspace = new Workspace();
        FolioSet.WriteTo(workspace);
        workspace.Text("lists.md", "# One\n\n## Two\n\n1. a\n1. b\n\n```mermaid\nflowchart TD\n  A --> B\n```\n\n[Site](https://example.com)\n");
        workspace.Text("Shared.html", $"""
            <!DOCTYPE html><html><head>{FolioMarkup.MarkerMeta}</head><body>
            {FolioPayload.Encode(new FolioPayload { Documents = [new FolioPayloadDocument { Entry = "guide.md", Text = "# Guide\n" }] })}
            </body></html>
            """);

        string[][] runs =
        [
            ["format", "lists.md"],
            ["format", "--check", "lists.md"],
            ["number", "--dry-run", "lists.md"],
            ["unnumber", "lists.md"],
            ["replace", "--find", "Two", "--with", "Three", "lists.md"],
            ["renumber-lists", "lists.md"],
            ["diagram-layout", "--direction", "left-to-right", "lists.md"],
            ["check", "lists.md", "docs/guide.md"],
            ["toc", "lists.md"],
            ["outline", "--json", "lists.md"],
            ["folio", "-o", "Set.zip", "docs/guide.md", "docs/setup.md"],
            ["folio", "-o", "Set/", "--shrink-images", "800", "docs/guide.md"],
            ["folio", "--dry-run", "--fetch-web-images", "-o", "Fetched.zip", "docs/guide.md"],
            ["unpack", "Shared.html", "--out-dir", "Unpacked"],
            ["themes", "list"],
            ["version"],
            ["--version"],
            ["--help"],
        ];

        using var listener = new NetworkListener();

        foreach (string[] run in runs)
        {
            CliResult result = await CliRunner.RunAsync(run, currentDirectory: workspace.Root, cancellationToken: Ct);

            result.ExitCode.ShouldBeOneOf([0, 1], $"marqora-cli {string.Join(' ', run)}\n{result}");
            listener.Connections.ShouldBeEmpty($"marqora-cli {string.Join(' ', run)} connected");
        }
    }

    /// <summary>"new HttpClient", or any other way of opening a connection, appears once in the tree: in FolioFetcher.FetchAsync.</summary>
    [Fact]
    public void The_only_http_client_is_built_inside_the_fetch_method()
    {
        List<string> found = [];

        foreach (string file in Directory.GetFiles(Repo.At("src"), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(Repo.Root, file).Replace('\\', '/');

            if (relative.Contains("/obj/", StringComparison.Ordinal) || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                if (Opener().IsMatch(lines[i]))
                {
                    found.Add($"{relative}:{i + 1}:{Member(lines, i)}");
                }
            }
        }

        found.ShouldNotBeEmpty("the scan finds the fetcher's own client, or it is scanning nothing");
        found.ShouldAllBe(f => f.StartsWith("src/PaulTechGuy.MQ.Folio/FolioFetcher.cs:", StringComparison.Ordinal) && f.EndsWith(":FetchAsync", StringComparison.Ordinal));
    }

    /// <summary>The member a line sits in: the nearest declaration above it at member indentation.</summary>
    private static string Member(string[] lines, int index)
    {
        for (int i = index; i >= 0; i--)
        {
            if (Declaration().Match(lines[i]) is { Success: true } match)
            {
                return match.Groups["name"].Value;
            }
        }

        return "?";
    }

    [GeneratedRegex(@"\bnew\s+(HttpClient|HttpClientHandler|SocketsHttpHandler|TcpClient|UdpClient|Socket|WebClient|ClientWebSocket)\s*[\(\{]")]
    private static partial Regex Opener();

    [GeneratedRegex(@"^    (public|private|internal|protected)\b[^=;]*?\b(?<name>\w+)\s*(<[^>]*>)?\(")]
    private static partial Regex Declaration();

    /// <summary>Every connection the runtime starts while this is alive: a socket connect or an HTTP request.</summary>
    private sealed class NetworkListener : EventListener
    {
        private readonly ConcurrentQueue<string> _connections = new();

        public IReadOnlyCollection<string> Connections => _connections;

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            if (eventSource.Name is "System.Net.Sockets" or "System.Net.Http")
            {
                EnableEvents(eventSource, EventLevel.Informational);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            if (eventData.EventName is "ConnectStart" or "RequestStart")
            {
                // OnEventSourceCreated can run inside the base constructor, before this field is
                // assigned; an event can only follow it, by which time the field is there.
                _connections?.Enqueue($"{eventData.EventSource.Name}/{eventData.EventName}");
            }
        }
    }
}
