// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// Carries files from a redirected launch to the window that will open them.
///
/// The two ends do not exist at the same time. The key instance has to subscribe to
/// <c>AppInstance.Activated</c> before the host is built - a second launch can arrive while
/// this one is still creating its window - so activations that land in that gap are held
/// here until <see cref="Attach"/> supplies somewhere to put them.
///
/// Activations arrive on a thread pool thread. Delivery is left on that thread; the handler
/// marshals to the UI thread itself, because it is the part that knows which dispatcher.
/// </summary>
public sealed class ActivationRouter
{
    private readonly Lock _gate = new();
    private readonly List<string> _pending = [];

    private Action<IReadOnlyList<string>>? _handler;

    /// <summary>Hands over files from a launch that redirected here.</summary>
    public void Post(IReadOnlyList<string> paths)
    {
        Action<IReadOnlyList<string>>? handler;

        lock (_gate)
        {
            handler = _handler;

            if (handler is null)
            {
                _pending.AddRange(paths);
                return;
            }
        }

        handler(paths);
    }

    /// <summary>
    /// Supplies the destination for activations, flushing anything that arrived first.
    /// </summary>
    public void Attach(Action<IReadOnlyList<string>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        List<string> buffered;

        lock (_gate)
        {
            _handler = handler;
            buffered = [.. _pending];
            _pending.Clear();
        }

        if (buffered.Count > 0)
        {
            handler(buffered);
        }
    }
}
