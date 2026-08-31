// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// On-disk envelope for the MRU list. The schema version lets a future release migrate
/// an older file instead of silently discarding it.
/// </summary>
internal sealed record RecentFilesDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public List<RecentFile> Items { get; init; } = [];
}
