// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// Serialization for the preferences file, which is not settings.json and does not want the
/// same options.
///
/// Two differences from <see cref="MarqoraJsonContext"/>, both load-bearing:
///
///   - No <c>DefaultIgnoreCondition</c>, so a null is written rather than omitted. Nulls are
///     meaningful here: an exported file that left <c>sourceFontFamily</c> out would be read
///     on the other machine as "says nothing about the source font", and that machine would
///     keep its own - the opposite of what exporting a machine with no font override means.
///     Writing every key also gives import a complete census of what this build knows, which
///     is what the report's "not in the file" line is worked out from, without a hand-kept
///     list of property names to drift out of step.
///   - <c>PropertyNameCaseInsensitive</c>, because a file that has been hand-edited, or
///     produced by some other tool, should not fail over the case of a key.
///
/// A separate context rather than options passed at the call site: source generation is
/// per-context, and reusing the settings context with different options would fall back to
/// reflection, which is the thing both contexts exist to avoid.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PreferencesDocument))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(JsonObject))]
internal sealed partial class PreferencesJsonContext : JsonSerializerContext;
