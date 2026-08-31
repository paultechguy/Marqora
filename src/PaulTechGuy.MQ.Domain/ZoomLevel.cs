// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// A clamped zoom percentage. Zoom is stored as an integer percentage rather than a
/// double so it round-trips through JSON and the JavaScript bridge without drift.
/// </summary>
public readonly record struct ZoomLevel
{
    public const int Minimum = 50;
    public const int Maximum = 500;
    public const int Default = 100;

    /// <summary>Steps the zoom commands walk through, so zooming feels predictable.</summary>
    private static readonly int[] Steps =
        [50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300, 350, 400, 450, 500];

    public ZoomLevel(int percent) => Percent = Math.Clamp(percent, Minimum, Maximum);

    public int Percent { get; }

    public double Scale => Percent / 100d;

    public static ZoomLevel Normal => new(Default);

    public bool IsDefault => Percent == Default;

    /// <summary>Next step up, or the current value if already at maximum.</summary>
    public ZoomLevel In()
    {
        // Copied to a local: a lambda inside a struct cannot capture 'this'.
        int current = Percent;
        return new ZoomLevel(Steps.FirstOrDefault(s => s > current, Maximum));
    }

    /// <summary>Next step down, or the current value if already at minimum.</summary>
    public ZoomLevel Out()
    {
        int current = Percent;
        return new ZoomLevel(Steps.LastOrDefault(s => s < current, Minimum));
    }

    public override string ToString() => $"{Percent}%";

    public static implicit operator int(ZoomLevel zoom) => zoom.Percent;
}
