// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Xml.Linq;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Architecture;

/// <summary>
/// CLI-ARC-01, enforced: the CLI and the app never reach each other, and none of Exporting,
/// WebRender and Imaging reaches WinUI - by any path, not only a direct reference. Read from the
/// project files themselves, so a reference added in Visual Studio fails here on the next run.
/// </summary>
public sealed class ArchitectureTests
{
    private const string App = "PaulTechGuy.MQ.App";
    private const string Cli = "PaulTechGuy.MQ.Cli";

    /// <summary>The shared projects that must build without WinUI, so the CLI can carry them.</summary>
    private static readonly string[] WithoutWinUI = ["PaulTechGuy.MQ.Exporting", "PaulTechGuy.MQ.WebRender", "PaulTechGuy.MQ.Imaging"];

    /// <summary>One project as the rules see it: what it references, and whether it is WinUI itself.</summary>
    internal sealed record ProjectNode(string Name, IReadOnlyList<string> References, bool UsesWinUI, string? TargetFramework);

    [Fact]
    public void The_projects_keep_the_dependency_rule()
    {
        Violations(Projects()).ShouldBeEmpty();
    }

    [Fact]
    public void Exporting_is_plain_net10_so_nothing_in_it_can_reach_Windows()
    {
        Projects()["PaulTechGuy.MQ.Exporting"].TargetFramework.ShouldBe("net10.0");
    }

    /// <summary>The rules are only worth having if they fail: each forbidden shape, on a made-up graph.</summary>
    [Fact]
    public void A_forbidden_reference_is_caught_directly_or_through_another_project()
    {
        Dictionary<string, ProjectNode> graph = new()
        {
            [App] = new(App, [Cli], UsesWinUI: true, null),
            [Cli] = new(Cli, ["PaulTechGuy.MQ.Exporting"], UsesWinUI: false, null),
            ["PaulTechGuy.MQ.Exporting"] = new("PaulTechGuy.MQ.Exporting", ["PaulTechGuy.MQ.Middle"], UsesWinUI: false, "net10.0"),
            ["PaulTechGuy.MQ.Middle"] = new("PaulTechGuy.MQ.Middle", [App], UsesWinUI: false, null),
            ["PaulTechGuy.MQ.WebRender"] = new("PaulTechGuy.MQ.WebRender", [], UsesWinUI: false, null),
            ["PaulTechGuy.MQ.Imaging"] = new("PaulTechGuy.MQ.Imaging", [], UsesWinUI: false, null),
        };

        string[] found = [.. Violations(graph)];

        found.ShouldContain(v => v.StartsWith("PaulTechGuy.MQ.App reaches PaulTechGuy.MQ.Cli", StringComparison.Ordinal));
        found.ShouldContain(v => v.StartsWith("PaulTechGuy.MQ.Cli reaches PaulTechGuy.MQ.App", StringComparison.Ordinal));
        found.ShouldContain(v => v.StartsWith("PaulTechGuy.MQ.Exporting reaches WinUI", StringComparison.Ordinal));
    }

    internal static IEnumerable<string> Violations(IReadOnlyDictionary<string, ProjectNode> projects)
    {
        if (Reaches(projects, Cli, App) is { } cliPath)
        {
            yield return $"{Cli} reaches {App}: {cliPath}";
        }

        if (Reaches(projects, App, Cli) is { } appPath)
        {
            yield return $"{App} reaches {Cli}: {appPath}";
        }

        foreach (string name in WithoutWinUI)
        {
            if (Walk(projects, name).FirstOrDefault(p => projects[p[^1]].UsesWinUI) is { } path)
            {
                yield return $"{name} reaches WinUI: {string.Join(" -> ", path)}";
            }
        }
    }

    private static string? Reaches(IReadOnlyDictionary<string, ProjectNode> projects, string from, string to) =>
        Walk(projects, from).FirstOrDefault(p => p[^1] == to) is { } path ? string.Join(" -> ", path) : null;

    /// <summary>Every project reachable from one, each with the path that reached it, the start included.</summary>
    private static IEnumerable<string[]> Walk(IReadOnlyDictionary<string, ProjectNode> projects, string start)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string[]>([[start]]);

        while (queue.TryDequeue(out string[]? path))
        {
            if (!seen.Add(path[^1]) || !projects.TryGetValue(path[^1], out ProjectNode? node))
            {
                continue;
            }

            yield return path;

            foreach (string reference in node.References)
            {
                queue.Enqueue([.. path, reference]);
            }
        }
    }

    /// <summary>Every project under src, read from its .csproj.</summary>
    private static Dictionary<string, ProjectNode> Projects()
    {
        Dictionary<string, ProjectNode> projects = new(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.GetFiles(Repo.At("src"), "*.csproj", SearchOption.AllDirectories))
        {
            XDocument project = XDocument.Load(file);
            string name = Path.GetFileNameWithoutExtension(file);

            string[] references = [.. project.Descendants("ProjectReference")
                .Select(r => Path.GetFileNameWithoutExtension((string?)r.Attribute("Include") ?? string.Empty))];

            bool winUI = project.Descendants("UseWinUI").Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                || project.Descendants("PackageReference").Any(r => ((string?)r.Attribute("Include") ?? string.Empty).StartsWith("Microsoft.WindowsAppSDK", StringComparison.OrdinalIgnoreCase));

            projects[name] = new ProjectNode(name, references, winUI, project.Descendants("TargetFramework").FirstOrDefault()?.Value.Trim());
        }

        projects.Keys.ShouldContain(App, "the app's project is where the rules expect it");
        projects.Keys.ShouldContain(Cli);

        return projects;
    }
}
