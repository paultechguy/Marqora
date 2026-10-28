// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using PaulTechGuy.MQ.Abstractions.Rendering;
using PaulTechGuy.MQ.WebRender;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Cli.Tests.Render;

/// <summary>
/// The shell bridge both hosts use (CLI-ARC-05), over a transport that records what is posted
/// instead of a WebView2: what a request looks like on the wire, which answer goes to which
/// request, and how a request ends when no answer comes.
/// </summary>
public sealed class ShellBridgeTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_request_is_posted_as_type_and_payload_in_camel_case()
    {
        var transport = new RecordingTransport();
        var bridge = new ShellBridge(transport);

        _ = bridge.DiagramPngAsync("abc", forCopy: true, Long, Ct);

        using JsonDocument posted = JsonDocument.Parse(transport.Posted.ShouldHaveSingleItem());
        posted.RootElement.GetProperty("type").GetString().ShouldBe("requestDiagramPng");

        JsonElement payload = posted.RootElement.GetProperty("payload");
        Guid.TryParse(payload.GetProperty("requestId").GetString(), out _).ShouldBeTrue();
        payload.GetProperty("hash").GetString().ShouldBe("abc");
        payload.GetProperty("copy").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Each_answer_goes_to_the_request_it_names_whatever_order_they_arrive_in()
    {
        var transport = new RecordingTransport();
        var bridge = new ShellBridge(transport);

        Task<BridgeOutcome<string>> first = bridge.RenderedHtmlAsync(Long, Ct);
        Task<BridgeOutcome<string>> second = bridge.ExportHtmlAsync("<p>x</p>", Long, Ct);

        // The second answered first, as other traffic in between would have it.
        Answer(bridge, "exportHtml", transport.RequestId(1), "html", "finished");
        Answer(bridge, "renderedHtml", transport.RequestId(0), "html", "on screen");

        (await first).Value.ShouldBe("on screen");
        (await second).Value.ShouldBe("finished");
    }

    [Fact]
    public async Task An_answer_is_read_from_the_field_its_reply_carries_it_in()
    {
        var transport = new RecordingTransport();
        var bridge = new ShellBridge(transport);

        Task<BridgeOutcome<string>> printing = bridge.PrintHtmlAsync(Long, Ct);
        Answer(bridge, "printHtml", transport.RequestId(0), "data", "<main/>");

        BridgeOutcome<string> outcome = await printing;
        outcome.Result.ShouldBe(BridgeResult.Rendered);
        outcome.Value.ShouldBe("<main/>");
    }

    [Fact]
    public void A_reply_the_bridge_owns_is_taken_even_when_nothing_waits_for_it_and_others_are_not()
    {
        var bridge = new ShellBridge(new RecordingTransport());

        // Stale, or for a request that timed out: still the bridge's, so the host skips it.
        Answer(bridge, "diagramSvg", Guid.NewGuid(), "data", "<svg/>").ShouldBeTrue();

        using JsonDocument ready = JsonDocument.Parse("{}");
        bridge.Accept("ready", ready.RootElement).ShouldBeFalse();
        bridge.Accept("reviewHtml", ready.RootElement).ShouldBeFalse();
    }

    [Fact]
    public async Task No_answer_in_time_is_timed_out_and_a_late_one_changes_nothing()
    {
        var transport = new RecordingTransport();
        var bridge = new ShellBridge(transport);

        BridgeOutcome<string> outcome = await bridge.RenderSettledAsync(TimeSpan.FromMilliseconds(50), Ct);

        outcome.Result.ShouldBe(BridgeResult.TimedOut);
        outcome.Value.ShouldBeNull();
        Answer(bridge, "renderSettled", transport.RequestId(0), "data", "settled").ShouldBeTrue();
    }

    [Fact]
    public async Task A_request_with_no_shell_to_post_to_fails_at_once_with_the_reason()
    {
        // What CoreWebView2Transport does when the preview has no WebView2 to post to.
        var bridge = new ShellBridge(new NoShellTransport());

        BridgeOutcome<string> outcome = await bridge.MathPngAsync(3, 1, Long, Ct);

        outcome.Result.ShouldBe(BridgeResult.Failed);
        outcome.Error.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Canceling_the_wait_ends_the_request_with_cancellation()
    {
        var bridge = new ShellBridge(new RecordingTransport());
        using var cancel = new CancellationTokenSource();

        Task<BridgeOutcome<string>> waiting = bridge.PrepareForPrintAsync(1.29, "Guide", Long, cancel.Token);
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(waiting);
    }

    private static bool Answer(ShellBridge bridge, string reply, Guid id, string field, string value)
    {
        using JsonDocument payload = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["requestId"] = id.ToString(),
            [field] = value,
        }));

        return bridge.Accept(reply, payload.RootElement);
    }

    private sealed class NoShellTransport : IShellTransport
    {
        public void Post(string json) => throw new InvalidOperationException("There is no shell to post to.");
    }

    private sealed class RecordingTransport : IShellTransport
    {
        public List<string> Posted { get; } = [];

        public void Post(string json) => Posted.Add(json);

        public Guid RequestId(int index)
        {
            using JsonDocument posted = JsonDocument.Parse(Posted[index]);
            return Guid.Parse(posted.RootElement.GetProperty("payload").GetProperty("requestId").GetString()!);
        }
    }
}
