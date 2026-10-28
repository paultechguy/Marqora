// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Web.WebView2.Core;

namespace PaulTechGuy.MQ.WebRender;

/// <summary>
/// The way a <see cref="ShellBridge"/> reaches the shell: one message out. Replies come back
/// through the host's own message handler, which hands each one to
/// <see cref="ShellBridge.Accept"/> before its own switch, so a message is read once.
/// </summary>
public interface IShellTransport
{
    /// <summary>
    /// Posts one message, already serialized. Throws when there is no shell to post to; the
    /// bridge reports that as <see cref="BridgeResult.Failed"/>.
    /// </summary>
    void Post(string json);
}

/// <summary>
/// The transport over a <see cref="CoreWebView2"/>, used by the live preview and the headless
/// host alike (CLI-ARC-05).
///
/// It takes the WebView2 as a function rather than an instance because the preview's changes:
/// recovering from a shell that stopped responding makes a new one, and a transport holding the
/// old one would post into a page nobody reads. Null means there is no shell to post to now.
/// </summary>
public sealed class CoreWebView2Transport(Func<CoreWebView2?> core) : IShellTransport
{
    public CoreWebView2Transport(CoreWebView2 core)
        : this(() => core)
    {
        ArgumentNullException.ThrowIfNull(core);
    }

    public void Post(string json)
    {
        CoreWebView2 target = core() ?? throw new InvalidOperationException("There is no shell to post to.");

        target.PostWebMessageAsJson(json);
    }
}
