// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Rendering;

/// <summary>How a request to the shell ended.</summary>
public enum BridgeResult
{
    /// <summary>The shell answered.</summary>
    Rendered,

    /// <summary>The shell did not answer in time. Each caller decides what that means.</summary>
    TimedOut,

    /// <summary>The request could not be sent: there was no shell to send it to.</summary>
    Failed,
}

/// <summary>
/// A request's outcome, and the shell's answer when there was one. The app turns a
/// <see cref="BridgeResult.TimedOut"/> into the degrade it always had; marqora-cli fails the
/// file (CLI-EX-05). The bridge and the renderers do neither, so both can share them.
///
/// Here rather than beside the bridge in WebRender because <see cref="IDocumentRenderer"/>
/// answers with it, and the export code that calls the renderer builds for any platform.
/// </summary>
public readonly record struct BridgeOutcome<T>(BridgeResult Result, T? Value, Exception? Error)
{
    public bool IsRendered => Result == BridgeResult.Rendered;
}

/// <summary>Makes a <see cref="BridgeOutcome{T}"/> of each kind.</summary>
public static class BridgeOutcome
{
    public static BridgeOutcome<T> Rendered<T>(T value) => new(BridgeResult.Rendered, value, null);

    public static BridgeOutcome<T> TimedOut<T>() => new(BridgeResult.TimedOut, default, null);

    public static BridgeOutcome<T> Failed<T>(Exception error) => new(BridgeResult.Failed, default, error);

    /// <summary>The same ending with a different answer, for a renderer that reshapes the shell's.</summary>
    public static BridgeOutcome<TResult> Map<T, TResult>(this BridgeOutcome<T> outcome, Func<T?, TResult> answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return outcome.IsRendered
            ? Rendered(answer(outcome.Value))
            : new BridgeOutcome<TResult>(outcome.Result, default, outcome.Error);
    }
}
