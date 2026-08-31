// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace PaulTechGuy.MQ.Domain;

/// <summary>One entry in the most-recently-used list.</summary>
public sealed record RecentFile
{
    public required string Path { get; init; }

    public required DateTimeOffset LastOpenedUtc { get; init; }

    /// <summary>Pinned entries survive trimming and sort above unpinned ones.</summary>
    public bool IsPinned { get; init; }

    [JsonIgnore]
    public string FileName => System.IO.Path.GetFileName(Path);

    [JsonIgnore]
    public string? DirectoryName => System.IO.Path.GetDirectoryName(Path);
}
