// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Cli.Rendering;
using PaulTechGuy.MQ.Cli.Tests.Harness;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Export;

/// <summary>CLI-NF-04: without the WebView2 runtime, the verbs that draw exit 5 and say how to install it; the rest work.</summary>
public sealed class MissingRuntimeTests
{
    [Fact]
    public async Task Export_pdf_exits_5_with_the_install_link_while_format_succeeds()
    {
        using var workspace = new Workspace();
        workspace.Text("a.md", "# A\n");

        CliResult export = await CliRunner.RunAsync(["export", "pdf", "-o", "a.pdf", "a.md"], currentDirectory: workspace.Root, webViewRuntime: () => null, cancellationToken: TestContext.Current.CancellationToken);
        CliResult format = await CliRunner.RunAsync(["format", "a.md"], currentDirectory: workspace.Root, webViewRuntime: () => null, cancellationToken: TestContext.Current.CancellationToken);

        export.ExitCode.ShouldBe(5, export.ToString());
        export.Error.ShouldContain("WebView2 runtime is not installed");
        export.Error.ShouldContain(RendererFactory.RuntimeLink);
        File.Exists(workspace.At("a.pdf")).ShouldBeFalse();

        format.ExitCode.ShouldBe(0, format.ToString());
    }
}
