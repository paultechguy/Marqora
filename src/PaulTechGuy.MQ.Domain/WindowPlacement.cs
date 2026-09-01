// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace PaulTechGuy.MQ.Domain;

/// <summary>Persisted window geometry, in physical pixels.</summary>
public sealed record WindowPlacement
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; } = 1_400;

    public int Height { get; set; } = 900;

    public bool IsMaximized { get; set; }

    /// <summary>True once the window has been placed at least once and has usable bounds.</summary>
    [JsonIgnore]
    public bool HasPosition => Width > 0 && Height > 0 && (X != 0 || Y != 0);

    public static WindowPlacement Default => new();
}
