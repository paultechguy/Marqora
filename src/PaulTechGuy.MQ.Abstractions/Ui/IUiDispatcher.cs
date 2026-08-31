// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Ui;

/// <summary>
/// Marshals work onto the UI thread.
///
/// Services below the UI use ConfigureAwait(false), which is correct for library code but
/// means their events can be raised on a thread-pool thread. Anything that ends up touching
/// a bound property has to come back through here first, or XAML throws RPC_E_WRONG_THREAD.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>True when the caller is already on the UI thread.</summary>
    bool IsOnUiThread { get; }

    /// <summary>Runs the action on the UI thread, immediately if already there.</summary>
    void Post(Action action);

    /// <summary>
    /// Runs the action on the UI thread after the current round of work, including the
    /// layout and render pass that follows it - and so after any focus XAML assigns during
    /// that pass.
    ///
    /// Never inline, which is the whole point of it: a caller uses this precisely because
    /// running now would be overruled a moment later.
    /// </summary>
    void PostAfterRender(Action action);
}
