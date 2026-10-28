// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Cli.Rendering;
using PaulTechGuy.MQ.Domain;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Harness;

/// <summary>
/// The real browser, for the tests that are about it: the one browser per run, its data folder,
/// Ctrl+C. They run one at a time, because the paged engine a run prints with is the process's
/// own, and two runs in one process would hand it each other's browser. Two runs at once are
/// tested as two processes, which is how they happen.
/// </summary>
[CollectionDefinition(nameof(BrowserRunsAlone), DisableParallelization = true)]
public sealed class BrowserRunsAlone;

/// <summary>
/// Stands in for the run's browser in the tests about everything around it: outputs, findings,
/// the dry run, a render that never finishes. Counts what it was asked, so a test can say a run
/// never asked at all.
/// </summary>
internal sealed class FakeRenderers : IRendererFactory
{
    /// <summary>The text a document carries to have its render never finish.</summary>
    public const string NeverFinishes = "<!-- never finishes -->";

    /// <summary>The text a document carries to be printed by the classic engine, as when the paged one cannot lay it out.</summary>
    public const string Classic = "<!-- classic engine -->";

    public int Made { get; private set; }

    public int Runs { get; private set; }

    public RenderSetup? Setup { get; private set; }

    /// <summary>As the environment's Renderers: records the setup and hands back this fake.</summary>
    public IRendererFactory Make(RenderSetup setup)
    {
        Made++;
        Setup = setup;

        return this;
    }

    public Task<T> RunAsync<T>(Func<IDocumentRenderer, Task<T>> work, CancellationToken cancellationToken)
    {
        Runs++;

        return work(new Renderer());
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>Renders anything but a document marked <see cref="NeverFinishes"/>, attached or detached, and prints a PDF of a few bytes.</summary>
    private sealed class Renderer : IDocumentRenderer
    {
        private bool _classic;

        public Task<BridgeOutcome<RenderedDocument>> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default)
        {
            // The PDF that follows is printed from this render, as the real host prints what it last rendered.
            _classic = request.Markdown.Contains(Classic, StringComparison.Ordinal);

            return Task.FromResult(request.Markdown.Contains(NeverFinishes, StringComparison.Ordinal)
                ? BridgeOutcome.TimedOut<RenderedDocument>()
                : BridgeOutcome.Rendered(new RenderedDocument("<h1>Rendered</h1>")));
        }

        public Task<byte[]?> DiagramPngAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult<byte[]?>(null);

        public Task<string?> DiagramSvgAsync(string hash, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<MathPicture?> MathPngAsync(int line, int ordinal, CancellationToken cancellationToken = default) => Task.FromResult<MathPicture?>(null);

        public Task<BridgeOutcome<string>> RenderDetachedAsync(string html, CancellationToken cancellationToken = default) =>
            Task.FromResult(html.Contains(NeverFinishes, StringComparison.Ordinal)
                ? BridgeOutcome.TimedOut<string>()
                : BridgeOutcome.Rendered(html));

        public async Task<PdfExportOutcome> PrintPdfAsync(string path, PdfPageSetup setup, string title, PaperFurniture furniture, CancellationToken cancellationToken = default)
        {
            await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes($"%PDF-1.7 {title}\n%%EOF\n"), cancellationToken);

            return new PdfExportOutcome(_classic ? PrintEngine.Classic : PrintEngine.Paged, []);
        }
    }
}
