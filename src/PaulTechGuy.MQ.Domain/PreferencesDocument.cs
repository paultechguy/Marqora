// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json.Nodes;

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// A preferences file, as written by Export and read by Import: an envelope saying where the
/// settings came from, wrapped around the settings themselves.
///
/// The envelope exists so that a file arriving from another machine can say what wrote it
/// before anything is done with it. <see cref="SchemaVersion"/> is the part that matters -
/// <see cref="AppVersion"/> and <see cref="ExportedFrom"/> are there for the person reading
/// the file or the import report, not for the code.
///
/// The version is not a gate. A file from a newer build is still imported for every setting
/// this build understands, and the ones it does not are named in the report. Refusing the
/// whole file over a version number would make the feature useless in the one situation it
/// exists for: two machines that are not on the same build yet.
///
/// <see cref="Preferences"/> is deliberately a <see cref="JsonObject"/> rather than an
/// <see cref="AppSettings"/>. Deserializing straight into the settings record would throw
/// away the two things the report is made of - which keys this build does not recognise, and
/// which it expected and did not find - because both look identical to "absent" once the
/// JSON has become an object. Keeping it as a tree means import can compare key for key.
///
/// The properties are <c>set</c> rather than <c>init</c> for the same reason as the ones on
/// <see cref="AppSettings"/>; the comment there has the detail.
/// </summary>
public sealed record PreferencesDocument
{
    /// <summary>What <see cref="Format"/> holds, so a file can be identified by looking at it.</summary>
    public const string FormatName = "marqora-preferences";

    /// <summary>
    /// The shape of the file, not the shape of the settings.
    ///
    /// It goes up when the envelope itself changes - a renamed member, a different nesting -
    /// and not when a preference is added, removed or renamed. Those are ordinary drift
    /// between builds and are what the import report is for.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>What every exported file is called before the stamp is added.</summary>
    public const string FileNamePrefix = "Marqora-preferences";

    /// <summary>
    /// Plain <c>.json</c> rather than an invented extension. The file is meant to be
    /// readable, and a settings file that Notepad and every diff tool already understand is
    /// worth more than an extension that only Marqora would recognise.
    /// </summary>
    public const string FileExtension = ".json";

    /// <summary>
    /// The name the export dialog opens on: the prefix, when it was taken, and the extension.
    ///
    /// Stamped so that exporting twice leaves two files rather than an overwrite prompt. The
    /// point of exporting is usually that something is about to change, and the copy taken
    /// beforehand is worth more than the one taken after - so the default must not quietly
    /// land on top of it.
    ///
    /// Big-endian and zero-padded, which is what makes a folder of these sort into the order
    /// they were taken in - the only ordering anybody wants from a stack of backups. Seconds
    /// are included because two exports a few moments apart are exactly what happens when the
    /// first one went somewhere the user did not mean.
    ///
    /// Local time, deliberately, though <see cref="ExportedUtc"/> inside the file is not: the
    /// name is read by a person deciding which file is the recent one, and 14:30 has to mean
    /// half past two to them. The unambiguous instant is recorded inside the envelope, where
    /// a machine reads it.
    ///
    /// Invariant format, so the name is the same ASCII on every machine. A culture with its
    /// own digits or its own date order would otherwise produce a file name that does not
    /// sort beside the others and may not survive being copied to a different machine.
    /// </summary>
    public static string SuggestedFileName(DateTimeOffset taken) =>
        FileNamePrefix
        + taken.ToLocalTime().ToString("'-'yyyy-MM-dd'-'HHmmss", CultureInfo.InvariantCulture)
        + FileExtension;

    public string Format { get; set; } = FormatName;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>The Marqora that wrote the file. Reported on import; never acted on.</summary>
    public string? AppVersion { get; set; }

    public DateTimeOffset ExportedUtc { get; set; }

    /// <summary>
    /// The machine the file came from, which is the whole point of a file that travels.
    /// Recorded so that a folder of these can be told apart without opening them.
    /// </summary>
    public string? ExportedFrom { get; set; }

    /// <summary>The preferences themselves, with the session's own state already stripped.</summary>
    public JsonObject? Preferences { get; set; }
}
