// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaulTechGuy.MQ.Folio;

/// <summary>One document's source, as a Folio carries it.</summary>
public sealed record FolioPayloadDocument
{
    public string Entry { get; set; } = string.Empty;

    /// <summary>The markdown itself, exactly as it was written into the Folio.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// The sources a reading copy carries inside itself, so that one file is both the page anyone
/// can read and the documents it was made from.
///
/// The images are not in here. They are already in the page as data URIs on the img elements,
/// and repeating them would add a third again to a file that is mostly pictures by weight - so
/// each img carries a <c>data-mq-asset</c> attribute naming where it belongs, and unpacking
/// reads the bytes back out of the src it is already sitting on. This lists the assets only so
/// that a Folio can say what it expects to find.
/// </summary>
public sealed record FolioPayload
{
    /// <summary>What <see cref="Format"/> holds, shared with the folder and zip manifest.</summary>
    public const string FormatName = FolioManifest.FormatName;

    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// The script type the payload rides in.
    ///
    /// Not <c>application/json</c>, and not anything a browser executes: an unknown type is
    /// neither run nor rendered, so to a reader the block is invisible. It is the one thing in
    /// the page a browser is asked to ignore completely.
    /// </summary>
    public const string ScriptType = "application/vnd.marqora.folio+json";

    /// <summary>
    /// The marker in the head that makes recognizing a Folio a cheap read.
    ///
    /// The payload sits at the end of a file that can be twenty megabytes; this sits in the
    /// first kilobyte, so deciding whether a dropped .html is a Folio never means reading one.
    /// </summary>
    public const string Marker = "marqora-folio";

    /// <summary>
    /// A ceiling on how many documents a Folio may claim to hold.
    ///
    /// Not a size limit - base64 shrinks by a quarter when decoded, so nothing here can be
    /// larger than the file it came out of. It is a guard against a file that says it holds a
    /// million documents and makes the app find out one allocation at a time.
    /// </summary>
    public const int MaximumDocuments = 2_000;

    public string Format { get; set; } = FormatName;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string? AppVersion { get; set; }

    public DateTimeOffset ExportedUtc { get; set; }

    public string? ExportedFrom { get; set; }

    public IReadOnlyList<FolioPayloadDocument> Documents { get; set; } = [];

    public IReadOnlyList<FolioManifestAsset> Assets { get; set; } = [];

    /// <summary>
    /// The script element to append to a Folio, payload and all.
    ///
    /// Base64, and that is not tidiness. A markdown document about HTML contains the characters
    /// that end a script element, and raw JSON here would be terminated by the first one - the
    /// rest of somebody's document then parsed as live markup and injected into the page. Base64
    /// leaves the block's body unable to contain the sequence at all.
    /// </summary>
    public static string Encode(FolioPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        string json = JsonSerializer.Serialize(payload, FolioPayloadJsonContext.Default.FolioPayload);
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        return $"<script type=\"{ScriptType}\">{encoded}</script>";
    }

    /// <summary>
    /// The payload out of a Folio's markup, or null when there is not one - which covers an
    /// ordinary web page, a Folio written by something that is not Marqora, and a file whose
    /// payload has been damaged in transit.
    ///
    /// Everything read back here was written by whoever made the Folio, which is not necessarily
    /// the person opening it. It is data to be checked, never instructions.
    /// </summary>
    public static FolioPayload? TryDecode(string html)
    {
        ArgumentNullException.ThrowIfNull(html);

        int opening = html.IndexOf($"<script type=\"{ScriptType}\">", StringComparison.OrdinalIgnoreCase);

        if (opening < 0)
        {
            return null;
        }

        int start = html.IndexOf('>', opening) + 1;
        int end = html.IndexOf("</script>", start, StringComparison.OrdinalIgnoreCase);

        if (start <= 0 || end < start)
        {
            return null;
        }

        try
        {
            byte[] json = Convert.FromBase64String(html[start..end].Trim());
            FolioPayload? payload = JsonSerializer.Deserialize(
                json, FolioPayloadJsonContext.Default.FolioPayload);

            if (payload is null
                || !string.Equals(payload.Format, FormatName, StringComparison.Ordinal)
                || payload.Documents.Count is 0 or > MaximumDocuments)
            {
                return null;
            }

            return payload;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether this markup announces itself as a Folio, judged from the head alone.
    ///
    /// Deliberately separate from <see cref="TryDecode"/> and deliberately cheap: it answers
    /// "is this worth opening properly?" for a file that has only been sniffed, not read.
    /// </summary>
    public static bool IsFolio(string head)
    {
        ArgumentNullException.ThrowIfNull(head);

        return head.Contains($"name=\"{Marker}\"", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The head element a Folio is recognized by.</summary>
public static class FolioMarkup
{
    public static string MarkerMeta => $"<meta name=\"{FolioPayload.Marker}\" content=\"1\" />";

    /// <summary>The attribute naming where an embedded image belongs when it is unpacked.</summary>
    public const string AssetAttribute = "data-mq-asset";
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FolioPayload))]
internal sealed partial class FolioPayloadJsonContext : JsonSerializerContext;
