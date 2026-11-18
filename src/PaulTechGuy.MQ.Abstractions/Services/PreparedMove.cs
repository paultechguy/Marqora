// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// The first half of moving path-keyed entries (read-only marks, pins) when a file or folder is
/// renamed: which stored keys sit at the old path or below it.
///
/// Collected while the old path still exists, because a key can be stored under either of two
/// spellings: the path as written, or the target a link at that path resolves to. Once the file
/// has moved, the link cannot be resolved any more, and the second spelling cannot be worked out.
/// </summary>
/// <param name="From">The old path, fully qualified as written.</param>
/// <param name="FromResolved">The old path with a link at it resolved, as the stores normalize it.</param>
/// <param name="Keys">The stored keys found at or below either spelling.</param>
public sealed record PreparedMove(string From, string FromResolved, IReadOnlyList<string> Keys)
{
    public bool IsEmpty => Keys.Count == 0;
}
