// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// On-disk envelope for the pinned documents. The schema version lets a future release migrate
/// an older file instead of silently discarding it.
///
/// Ordinary setters rather than <c>init</c>, for the reason <c>AppSettings</c> gives at length:
/// with <c>init</c> the source generator treats every property as a constructor parameter and
/// assigns all of them from an argument array, so a key absent from the file arrives as
/// <c>default</c> and quietly wipes the initializer.
/// </summary>
internal sealed record DocumentPinsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<string> Paths { get; set; } = [];
}
