// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using PaulTechGuy.MQ.Services;

namespace PaulTechGuy.MQ.App;

/// <summary>
/// The version of the running build, in one place.
///
/// Read once: it comes from an attribute baked into the assembly, and both the About box and
/// the first-run check would otherwise reach for it their own way and be free to disagree. How it
/// is read lives in <see cref="ProductVersion"/>, which marqora-cli reads its own version through
/// too, so the two executables of one build cannot report different versions.
/// </summary>
internal static class AppVersion
{
    public static string Current { get; } = ProductVersion.Of(typeof(AppVersion).Assembly);
}
