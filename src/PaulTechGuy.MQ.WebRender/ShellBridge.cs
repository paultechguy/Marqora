// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using PaulTechGuy.MQ.Abstractions.Rendering;

namespace PaulTechGuy.MQ.WebRender;

/// <summary>
/// The requests both hosts make of the shell to get a document out of it, written once
/// (CLI-ARC-05): their message names, their payloads, the reply each is answered with and the
/// field the answer is in, and the matching of a reply to its request by id.
///
/// The live preview and the headless host each own a <see cref="CoreWebView2Transport"/> and one
/// of these, and hand every message the shell sends to <see cref="Accept"/> before reading it
/// themselves. What the bridge does not own - the preview's tabs, edits, selection, review and
/// the rest - stays with the preview, because only it ever asks.
///
/// Everything happens on the thread that owns the WebView2, the UI thread in the app and the
/// pump's in the CLI, which is why one plain dictionary is enough.
/// </summary>
public sealed class ShellBridge(IShellTransport transport)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Each reply the bridge owns, and the field its answer is in.</summary>
    private static readonly Dictionary<string, string> Replies = new(StringComparer.Ordinal)
    {
        ["renderedHtml"] = "html",
        ["exportHtml"] = "html",
        ["printHtml"] = "data",
        ["diagramPng"] = "data",
        ["diagramSvg"] = "data",
        ["mathPng"] = "data",
        ["outputReady"] = "data",
        ["renderSettled"] = "data",
    };

    private readonly Dictionary<Guid, TaskCompletionSource<string>> _requests = [];

    /// <summary>
    /// The message envelope every host posts, so the bridge's requests and a host's own
    /// messages are written the same way: <c>{ "type": ..., "payload": ... }</c>, camel case.
    /// </summary>
    public static string Serialize(string type, object payload) =>
        JsonSerializer.Serialize(new { type, payload }, JsonOptions);

    /// <summary>
    /// Takes a reply the bridge is waiting for. True when the message was one of the bridge's
    /// replies, answered or stale, so the host's own switch can skip it.
    /// </summary>
    public bool Accept(string type, JsonElement payload)
    {
        if (!Replies.TryGetValue(type, out string? field))
        {
            return false;
        }

        if (Guid.TryParse(Read(payload, "requestId"), out Guid id)
            && _requests.TryGetValue(id, out TaskCompletionSource<string>? waiting))
        {
            waiting.TrySetResult(Read(payload, field));
        }

        return true;
    }

    // ------------------------------------------------------------------ the requests

    /// <summary>The active document's rendered markup, as the preview shows it.</summary>
    public Task<BridgeOutcome<string>> RenderedHtmlAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        AskAsync("requestRenderedHtml", id => new { requestId = id }, timeout, cancellationToken);

    /// <summary>
    /// Markup Markdig produced, finished for export off screen: diagrams, math and highlighting
    /// applied in a container of the shell's own, never in the preview.
    /// </summary>
    public Task<BridgeOutcome<string>> ExportHtmlAsync(string html, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        AskAsync("requestExportHtml", id => new { requestId = id, html }, timeout, cancellationToken);

    /// <summary>The active document's print markup: laid-out math, light drawings, no comment marks.</summary>
    public Task<BridgeOutcome<string>> PrintHtmlAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        AskAsync("requestPrintHtml", id => new { requestId = id }, timeout, cancellationToken);

    /// <summary>One diagram as a PNG, base64; empty when the shell had none to give.</summary>
    public Task<BridgeOutcome<string>> DiagramPngAsync(string hash, bool forCopy, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        AskAsync("requestDiagramPng", id => new { requestId = id, hash, copy = forCopy }, timeout, cancellationToken);

    /// <summary>One diagram's light drawing as SVG; empty when the shell had none to give.</summary>
    public Task<BridgeOutcome<string>> DiagramSvgAsync(string hash, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        AskAsync("requestDiagramSvg", id => new { requestId = id, hash }, timeout, cancellationToken);

    /// <summary>One equation as "depth|base64 PNG"; empty when the shell had none to give.</summary>
    public Task<BridgeOutcome<string>> MathPngAsync(int line, int ordinal, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        AskAsync("requestMathPng", id => new { requestId = id, line, ordinal }, timeout, cancellationToken);

    /// <summary>
    /// Readies the active document for the classic print path: the page's shape and title set,
    /// answered once every diagram carries its light drawing.
    /// </summary>
    public Task<BridgeOutcome<string>> PrepareForPrintAsync(double pageRatio, string title, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        AskAsync("prepareForPrint", id => new { requestId = id, pageRatio, title }, timeout, cancellationToken);

    /// <summary>Answered once the active document's render has settled: diagrams drawn, math laid out.</summary>
    public Task<BridgeOutcome<string>> RenderSettledAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        AskAsync("requestRenderSettled", id => new { requestId = id }, timeout, cancellationToken);

    // ------------------------------------------------------------------ matching

    private async Task<BridgeOutcome<string>> AskAsync(
        string message,
        Func<Guid, object> payload,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _requests[id] = completion;

        try
        {
            try
            {
                transport.Post(Serialize(message, payload(id)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return BridgeOutcome.Failed<string>(ex);
            }

            Task finished = await Task.WhenAny(completion.Task, Task.Delay(timeout, cancellationToken))
                .ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();

            return finished == completion.Task
                ? BridgeOutcome.Rendered(await completion.Task.ConfigureAwait(true))
                : BridgeOutcome.TimedOut<string>();
        }
        finally
        {
            _requests.Remove(id);
        }
    }

    private static string Read(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
