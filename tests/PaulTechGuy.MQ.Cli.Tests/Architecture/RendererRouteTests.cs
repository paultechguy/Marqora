// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.RegularExpressions;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using PaulTechGuy.MQ.Exporting;
using PaulTechGuy.MQ.WebRender;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Architecture;

/// <summary>
/// CLI-ARC-04, held: every export reaches rendering through <see cref="IDocumentRenderer"/>, the
/// interface the live preview and the headless host both implement, and never through the
/// preview host's own members. An export that asked the preview directly would be an export the
/// CLI could not make the same way.
///
/// The export work itself is the four jobs in Exporting (WP-M2-06), which builds for any
/// platform, so the only route to rendering it can see is the interface; each job is handed its
/// renderer per run. The app is read from the source, as the test project cannot reference
/// WinUI: its export methods are dialogs, a job and a report, and nothing else.
/// </summary>
public sealed partial class RendererRouteTests
{
    private static readonly string AppViewModels = Path.Combine("src", "PaulTechGuy.MQ.App", "ViewModels");

    /// <summary>The app's four exports, and the job each runs.</summary>
    private static readonly (string Method, string Job)[] ExportMethods =
    [
        ("WriteHtmlExportAsync", "_htmlJob"),
        ("WriteFolioPageAsync", "_folioPageJob"),
        ("WriteWordExportAsync", "_wordJob"),
        ("WritePdfExportAsync", "_pdfJob"),
    ];

    /// <summary>What the view model did itself before the jobs, and must not do again.</summary>
    private static readonly string[] MovedIntoJobs =
    [
        "RenderedHtmlForExportAsync",
        "RequestDiagramPngAsync",
        "RequestDiagramSvgForExportAsync",
        "RequestMathPngForExportAsync",
        "PdfPictureIssuesAsync",
    ];

    private static readonly Type[] Jobs = [typeof(HtmlExportJob), typeof(PdfExportJob), typeof(WordExportJob), typeof(FolioPageJob)];

    [Fact]
    public void Every_export_path_in_the_app_is_found()
    {
        // If this fails, a method was renamed and the test below would pass by reading nothing.
        foreach ((string method, _) in ExportMethods)
        {
            MethodBody(method).ShouldNotBeNull($"{method} was not found in {AppViewModels}");
        }
    }

    [Fact]
    public void No_export_path_in_the_app_asks_the_preview_host()
    {
        var found = ExportMethods
            .Where(export => MethodBody(export.Method) is { } body && PreviewHostCall().IsMatch(body))
            .Select(export => export.Method)
            .ToList();

        found.ShouldBeEmpty();
    }

    [Fact]
    public void Every_app_export_runs_its_job_with_the_renderer()
    {
        var found = ExportMethods
            .Where(export => MethodBody(export.Method) is not { } body
                || !body.Contains(export.Job, StringComparison.Ordinal)
                || !body.Contains(".RunAsync(", StringComparison.Ordinal)
                || !body.Contains("_documentRenderer", StringComparison.Ordinal))
            .Select(export => export.Method)
            .ToList();

        found.ShouldBeEmpty();
    }

    [Fact]
    public void The_view_model_keeps_no_export_work_of_its_own()
    {
        MovedIntoJobs.Where(method => MethodBody(method) is not null).ShouldBeEmpty();
    }

    [Fact]
    public void Every_job_is_handed_its_renderer_and_answers_with_an_outcome()
    {
        foreach (Type job in Jobs)
        {
            MethodInfo run = job.GetMethod("RunAsync").ShouldNotBeNull($"{job.Name} has no RunAsync");

            run.GetParameters()[0].ParameterType.ShouldBe(typeof(IDocumentRenderer), job.Name);
            run.ReturnType.ShouldBe(typeof(Task<ExportOutcome>), job.Name);

            // Nothing else in reach of a job could render: its constructor takes no host.
            job.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Where(p => typeof(IDocumentRenderer).IsAssignableFrom(p.ParameterType)
                    || p.ParameterType.Name.Contains("Host", StringComparison.Ordinal))
                .ShouldBeEmpty(job.Name);
        }
    }

    [Fact]
    public void The_preview_host_offers_nothing_only_an_export_would_ask_for()
    {
        string[] members = [.. typeof(IPreviewHost).GetMethods().Select(m => m.Name)];

        members.ShouldNotContain("RenderForExportAsync");
        members.ShouldNotContain("RequestMathPngAsync");
        members.ShouldNotContain("ExportPdfAsync");

        typeof(IPreviewHost).GetMethods()
            .Where(m => m.ReturnType == typeof(Task<PdfExportOutcome>) || m.ReturnType == typeof(Task<MathPicture?>))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Both_renderers_implement_the_interface()
    {
        typeof(IDocumentRenderer).IsAssignableFrom(typeof(HeadlessRenderHost)).ShouldBeTrue();

        string preview = File.ReadAllText(Repo.At(Path.Combine("src", "PaulTechGuy.MQ.App", "Services", "WebViewPreviewHost.cs")));

        PreviewDeclaration().IsMatch(preview).ShouldBeTrue();
    }

    [Fact]
    public void Nothing_in_Exporting_names_the_preview_host()
    {
        var found = Directory.EnumerateFiles(Repo.At(Path.Combine("src", "PaulTechGuy.MQ.Exporting")), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && File.ReadAllText(file).Contains("IPreviewHost", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(Repo.Root, file))
            .ToList();

        found.ShouldBeEmpty();
    }

    /// <summary>The scan finds a call on the host when there is one: the shape it looks for, on made-up bodies.</summary>
    [Theory]
    [InlineData("string html = await _host.GetRenderedHtmlAsync();", true)]
    [InlineData("if (_host is not { } host) { return; }", true)]
    [InlineData("await _documentRenderer.RenderAsync(request);", false)]
    [InlineData("_hostess.Wave();", false)]
    public void A_call_on_the_preview_host_is_recognized(string body, bool expected)
    {
        PreviewHostCall().IsMatch(body).ShouldBe(expected);
    }

    /// <summary>
    /// The body of a method in the view model's files, from its opening brace to the one that
    /// closes it, or null when no file declares it. Braces in strings and comments are counted
    /// too; the export methods carry none that are unbalanced.
    /// </summary>
    private static string? MethodBody(string name)
    {
        var declaration = new Regex($@"private (?:async )?Task(?:<[^>]+>)? {name}\(");

        foreach (string file in Directory.EnumerateFiles(Repo.At(AppViewModels), "MainViewModel*.cs"))
        {
            string text = File.ReadAllText(file);

            if (declaration.Match(text) is not { Success: true } match)
            {
                continue;
            }

            int open = text.IndexOf('{', match.Index);
            int depth = 0;

            for (int i = open; i < text.Length; i++)
            {
                depth += text[i] switch { '{' => 1, '}' => -1, _ => 0 };

                if (depth == 0)
                {
                    return text[open..(i + 1)];
                }
            }
        }

        return null;
    }

    [GeneratedRegex(@"\b_host\b")]
    private static partial Regex PreviewHostCall();

    [GeneratedRegex(@"class WebViewPreviewHost\s*:[^{]*\bIDocumentRenderer\b")]
    private static partial Regex PreviewDeclaration();
}
