// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using PaulTechGuy.MQ.Abstractions.Ui;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// <see cref="IUiDispatcher"/> over the window's <see cref="DispatcherQueue"/>.
///
/// The queue is taken from the window rather than captured at construction, because the
/// container may build this instance from a thread that is not the UI thread.
/// </summary>
public sealed class UiDispatcher(WindowContext window, ILogger<UiDispatcher> logger) : IUiDispatcher
{
    private DispatcherQueue? Queue => window.Window?.DispatcherQueue;

    public bool IsOnUiThread => Queue?.HasThreadAccess ?? false;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        DispatcherQueue? queue = Queue;

        if (queue is null)
        {
            // Before the window exists there is nothing bound to update, so running inline
            // is both safe and the only option.
            RunGuarded(action);
            return;
        }

        if (queue.HasThreadAccess)
        {
            RunGuarded(action);
            return;
        }

        if (!queue.TryEnqueue(DispatcherQueuePriority.Normal, () => RunGuarded(action)))
        {
            logger.LogWarning("Could not queue work to the UI thread; the queue is shutting down.");
        }
    }

    public void PostAfterRender(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        DispatcherQueue? queue = Queue;

        if (queue is null)
        {
            // Before the window exists there is no render pass to wait for and nothing on
            // screen that could overrule the action, so inline is safe here even though it
            // is exactly what this method exists to avoid.
            RunGuarded(action);
            return;
        }

        if (!queue.TryEnqueue(DispatcherQueuePriority.Low, () => RunGuarded(action)))
        {
            logger.LogWarning("Could not queue deferred work to the UI thread; the queue is shutting down.");
        }
    }

    /// <summary>
    /// Queued work runs outside any caller's try/catch, so an exception here would reach
    /// the global handler with no useful context. Failures are logged at the source instead.
    /// </summary>
    private void RunGuarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A UI-thread callback failed.");
        }
    }
}
