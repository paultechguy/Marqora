// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace PaulTechGuy.MQ.Domain;

/// <summary>Persisted window geometry, in physical pixels.</summary>
public sealed record WindowPlacement
{
    public int X { get; init; }

    public int Y { get; init; }

    public int Width { get; init; } = 1_400;

    public int Height { get; init; } = 900;

    public bool IsMaximized { get; init; }

    /// <summary>True once the window has been placed at least once and has usable bounds.</summary>
    [JsonIgnore]
    public bool HasPosition => Width > 0 && Height > 0 && (X != 0 || Y != 0);

    public static WindowPlacement Default => new();
}
