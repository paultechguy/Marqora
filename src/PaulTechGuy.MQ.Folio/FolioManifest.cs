// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json.Serialization;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.Folio;

/// <summary>One document, as the manifest lists it.</summary>
public sealed record FolioManifestEntry
{
    /// <summary>Where it sits inside the Folio. Forward-slashed, relative to the root.</summary>
    public string Entry { get; set; } = string.Empty;

    /// <summary>True when a reference in this document had to be repointed on the way out.</summary>
    public bool Rewritten { get; set; }
}

/// <summary>One image, as the manifest lists it.</summary>
public sealed record FolioManifestAsset
{
    public string Entry { get; set; } = string.Empty;

    /// <summary>
    /// True when this image was collected from outside its document's folder, or renamed to
    /// avoid a clash. False means the path in the document is the one the author wrote.
    /// </summary>
    public bool Relocated { get; set; }
}

/// <summary>
/// The envelope: what wrote this Folio, when, and what is inside it.
///
/// The shape follows the preferences file next door, for the same reasons and with the same
/// rule about versions - a Folio written by a newer build is still read for everything this
/// build understands, because refusing a whole file over a number would break the one situation
/// the format exists for.
///
/// <b>No absolute paths are recorded.</b> The obvious thing to store beside each entry is where
/// it came from, and it is the one thing that must not be: a Folio is a file that gets emailed,
/// and "C:\Users\someone\Documents\..." tells the recipient the author's user name and how they
/// organize their disk. The entry names are enough to unpack, which makes the author's folder
/// structure something they choose to share rather than something the format leaks for them.
///
/// The properties are <c>set</c> rather than <c>init</c> for the same reason as the ones on
/// <see cref="Domain.AppSettings"/>: the serializer's source generator treats an init-only
/// member as a constructor parameter and overwrites every default with <c>default(T)</c>.
/// </summary>
public sealed record FolioManifest
{
    /// <summary>What <see cref="Format"/> holds, so a file can be identified by looking at it.</summary>
    public const string FormatName = "marqora-folio";

    /// <summary>
    /// The shape of the envelope, not the shape of what it carries. It goes up when a member is
    /// renamed or nested differently, and not when a document or an image is added.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The name of the manifest inside a Folio.
    ///
    /// Says what it is rather than only which feature made it. A recipient browsing an unpacked
    /// folder meets this file beside their documents and gets one word of explanation from the
    /// name alone - and the first line inside repeats it as <c>"format": "marqora-folio"</c>.
    /// </summary>
    public const string FileName = "folio-manifest.json";

    public string Format { get; set; } = FormatName;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>The Marqora that wrote it. Reported to the reader; never acted on.</summary>
    public string? AppVersion { get; set; }

    public DateTimeOffset ExportedUtc { get; set; }

    /// <summary>The machine it came from, so a folder of these can be told apart.</summary>
    public string? ExportedFrom { get; set; }

    public IReadOnlyList<FolioManifestEntry> Documents { get; set; } = [];

    public IReadOnlyList<FolioManifestAsset> Assets { get; set; } = [];

    /// <summary>The manifest describing a planned Folio.</summary>
    public static FolioManifest For(FolioPlan plan, string? appVersion, string? machineName)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new FolioManifest
        {
            AppVersion = appVersion,
            ExportedUtc = DateTimeOffset.UtcNow,
            ExportedFrom = machineName,
            Documents = [.. plan.Documents.Select(d => new FolioManifestEntry
            {
                Entry = d.EntryName,
                Rewritten = d.Rewritten,
            })],
            Assets = [.. plan.Assets.Select(a => new FolioManifestAsset
            {
                Entry = a.EntryName,
                Relocated = a.Relocated,
            })],
        };
    }

    /// <summary>
    /// The name to offer for a Folio built from these documents: the first document's own name
    /// when there is one, the folder's name when several came from the same place, and a stamped
    /// fallback otherwise.
    ///
    /// Stamped like the preferences export, and for the same reason - sharing the same set twice
    /// should leave two files rather than an overwrite prompt.
    /// </summary>
    public static string SuggestedName(FolioPlan plan, DateTimeOffset taken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Documents.Count == 1)
        {
            return Path.GetFileNameWithoutExtension(plan.Documents[0].EntryName);
        }

        string[] folders = [.. plan.Documents
            .Select(d => Path.GetDirectoryName(d.SourcePath))
            .OfType<string>()
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        if (folders.Length == 1 && Path.GetFileName(folders[0]) is { Length: > 0 } name)
        {
            return name;
        }

        return "Folio" + taken.ToLocalTime().ToString(
            "'-'yyyy-MM-dd'-'HHmmss", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// Source-generated serialization for the manifest, so there is no startup reflection and the
/// layer stays trim-friendly - the same arrangement the settings and preferences files use.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FolioManifest))]
internal sealed partial class FolioJsonContext : JsonSerializerContext;
