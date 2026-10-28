// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Architecture;

/// <summary>
/// CLI-ARC-05, held: the requests the live preview and the headless host both make of the shell
/// are written once, in ShellBridge. A request name written anywhere else in a host of the shell,
/// or a reply handled in a <c>case</c> of its own, is a second copy of a message shape, and the
/// two would drift apart without either noticing.
///
/// A host of the shell is a file that loads <c>ShellUri</c>, the page app.js runs in. The
/// cheatsheet and diagram windows load pages of their own, whose small protocols happen to
/// share two of these names (their print markup and a diagram's PNG) with no request id; they
/// are not the shell's messages, and not this test's.
/// </summary>
public sealed partial class BridgeShapeTests
{
    private static readonly string Bridge = Path.Combine("src", "PaulTechGuy.MQ.WebRender", "ShellBridge.cs");

    /// <summary>The request names, as ShellBridge writes them.</summary>
    private static string[] Requests() =>
        [.. RequestName().Matches(File.ReadAllText(Repo.At(Bridge))).Select(m => m.Groups[1].Value).Distinct()];

    /// <summary>The replies, from the table ShellBridge reads them by.</summary>
    private static string[] Replies() =>
        [.. ReplyName().Matches(File.ReadAllText(Repo.At(Bridge))).Select(m => m.Groups[1].Value)];

    [Fact]
    public void The_bridge_names_the_requests_and_replies_it_owns()
    {
        // If this fails, the patterns below no longer read ShellBridge, and the other tests
        // would pass by finding nothing.
        Requests().ShouldContain("requestPrintHtml");
        Requests().ShouldContain("prepareForPrint");
        Requests().Length.ShouldBe(8);
        Replies().ShouldBe(["renderedHtml", "exportHtml", "printHtml", "diagramPng", "diagramSvg", "mathPng", "outputReady", "renderSettled"]);
    }

    [Fact]
    public void No_request_name_is_written_outside_the_bridge()
    {
        string[] requests = Requests();

        var found = SourceFiles()
            .SelectMany(file => requests
                .Where(request => File.ReadAllText(file).Contains($"\"{request}\"", StringComparison.Ordinal))
                .Select(request => $"{Path.GetRelativePath(Repo.Root, file)}: \"{request}\""))
            .ToList();

        found.ShouldBeEmpty();
    }

    [Fact]
    public void No_reply_is_handled_outside_the_bridge()
    {
        string[] replies = Replies();

        var found = SourceFiles()
            .SelectMany(file => replies
                .Where(reply => File.ReadAllText(file).Contains($"case \"{reply}\"", StringComparison.Ordinal))
                .Select(reply => $"{Path.GetRelativePath(Repo.Root, file)}: case \"{reply}\""))
            .ToList();

        found.ShouldBeEmpty();
    }

    [Fact]
    public void Both_hosts_of_the_shell_are_scanned()
    {
        string[] hosts = [.. SourceFiles().Select(file => Path.GetFileName(file))];

        hosts.ShouldContain("WebViewPreviewHost.cs");
        hosts.ShouldContain("HeadlessRenderHost.cs");
    }

    /// <summary>Every C# file that loads the shell, other than the bridge itself.</summary>
    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Repo.At("src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.EndsWith(Bridge, StringComparison.OrdinalIgnoreCase)
                && File.ReadAllText(file).Contains(".ShellUri", StringComparison.Ordinal));

    [GeneratedRegex("""AskAsync\("([A-Za-z]+)",""")]
    private static partial Regex RequestName();

    [GeneratedRegex("""\["([A-Za-z]+)"\] = "(?:html|data)",""")]
    private static partial Regex ReplyName();
}
