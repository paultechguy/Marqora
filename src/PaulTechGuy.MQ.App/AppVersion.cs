// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;

namespace PaulTechGuy.MQ.App;

/// <summary>
/// The version of the running build, in one place.
///
/// Read once: it comes from an attribute baked into the assembly, and both the About box and
/// the first-run check would otherwise reach for it their own way and be free to disagree.
/// </summary>
internal static class AppVersion
{
    /// <summary>
    /// The informational version when the build stamped one, otherwise the assembly version.
    /// The suffix after a plus sign is the source revision and is noise in both places this
    /// is used.
    /// </summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        Assembly assembly = typeof(AppVersion).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
