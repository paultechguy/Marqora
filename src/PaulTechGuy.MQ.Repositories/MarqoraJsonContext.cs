// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// Source-generated serialization metadata. Using a context rather than reflection keeps
/// the persistence layer trim- and AOT-friendly and avoids startup reflection cost.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(RecentFilesDocument))]
internal sealed partial class MarqoraJsonContext : JsonSerializerContext;
