// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>
/// CLI-EX-WORD-01: export word carries what only a browser can make - the diagram's picture, the
/// equation KaTeX wrote, the code's colors - and takes its page from the settings. Each file is
/// made by a real browser and read back as the package it is.
/// </summary>
[Collection(nameof(BrowserRunsAlone))]
public sealed class ExportWordTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string Document = "# Word\n\n```mermaid\nflowchart LR\n  A --> B\n```\n\n$$\nx^2 + 1\n$$\n\n```csharp\nreturn 42;\n```\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_diagram_is_a_picture_the_math_an_equation_and_the_code_in_its_colors()
    {
        using var workspace = new Workspace();
        workspace.Text("doc.md", Document);

        CliResult result = await CliRunner.RunAsync(["export", "word", "doc.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.Error.ShouldNotContain("[the-diagram-could-not-be-drawn]");

        using ZipArchive package = ZipFile.OpenRead(workspace.At("doc.docx"));
        XDocument document = MainPart(package);

        package.Entries.ShouldContain(e => e.FullName.EndsWith(".png", StringComparison.Ordinal), "the diagram's picture");
        document.ToString().ShouldContain("oMath", Case.Sensitive, "the equation, as Word's own");

        // Without the render, the code would go in as plain text: the keyword's color is the
        // render's highlighting, carried over.
        XElement keyword = document.Descendants(W + "r").First(r => r.Element(W + "t")?.Value == "return");
        keyword.Element(W + "rPr")?.Element(W + "color").ShouldNotBeNull("the keyword's color");
    }

    [Fact]
    public async Task The_page_is_the_word_setup_the_settings_name()
    {
        using var workspace = new Workspace();
        workspace.Text("doc.md", "# Landscape\n");

        string settings = workspace.Copy(Path.Combine("tests", "baselines", "cli", "preferences.json"), "a4.json");
        string text = await File.ReadAllTextAsync(settings, Ct);
        text = Regex.Replace(text, "(\"docxSetup\": \\{\\s*\"paper\": )\"Letter\"(,\\s*\"orientation\": )\"Portrait\"", "$1\"A4\"$2\"Landscape\"");
        await File.WriteAllTextAsync(settings, text, Ct);

        CliResult result = await CliRunner.RunAsync(["export", "word", "--settings", "a4.json", "doc.md"], currentDirectory: workspace.Root, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());

        using ZipArchive package = ZipFile.OpenRead(workspace.At("doc.docx"));
        XElement size = MainPart(package).Descendants(W + "pgSz").Single();

        // A4 landscape in twentieths of a point: 297 mm across, 210 mm down.
        size.Attribute(W + "w")!.Value.ShouldBe("16838");
        size.Attribute(W + "h")!.Value.ShouldBe("11906");
        size.Attribute(W + "orient")!.Value.ShouldBe("landscape");
    }

    [Fact]
    public async Task A_dry_run_names_the_word_setup_and_writes_nothing()
    {
        using var workspace = new Workspace();
        workspace.Text("doc.md", "# Dry\n");

        CliResult result = await CliRunner.RunAsync(["export", "word", "--dry-run", "doc.md"], currentDirectory: workspace.Root, renderers: new FakeRenderers().Make, cancellationToken: Ct);

        result.ExitCode.ShouldBe(0, result.ToString());
        result.Out.ShouldContain("Page:       Letter, portrait, normal margins");
        File.Exists(workspace.At("doc.docx")).ShouldBeFalse();
    }

    private static XDocument MainPart(ZipArchive package)
    {
        using Stream stream = package.GetEntry("word/document.xml")!.Open();

        return XDocument.Load(stream);
    }
}
