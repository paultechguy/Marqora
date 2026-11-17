// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>How a rename or a delete made from the folder panel went.</summary>
/// <param name="Reason">Why it did not happen, written for the panel's notice line. Null on success.</param>
/// <param name="Permanent">For a delete: the item was deleted outright rather than recycled.</param>
public sealed record FileOperationResult(bool Succeeded, string? Reason = null, bool Permanent = false)
{
    public static FileOperationResult Done { get; } = new(true);

    public static FileOperationResult Refused(string reason) => new(false, reason);
}
