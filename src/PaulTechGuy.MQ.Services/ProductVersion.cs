// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// The version a Marqora executable reports, read one way for both of them.
///
/// Marqora.exe and marqora-cli.exe are built from one Directory.Build.props, so each one's own
/// assembly carries the same version; reading it the same way in both is what makes
/// "marqora-cli version" the app's version from the same build (CLI-VB-VER-01). Moved here from
/// the app's AppVersion, which now calls it.
/// </summary>
public static class ProductVersion
{
    /// <summary>
    /// The informational version when the build stamped one, otherwise the assembly version. The
    /// suffix after a plus sign is the source revision, which is noise to anyone reading a version.
    /// </summary>
    public static string Of(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

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
