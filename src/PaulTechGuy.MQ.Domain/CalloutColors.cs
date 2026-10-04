// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>Which of the five GitHub callouts a block is.</summary>
public enum CalloutKind
{
    Note,
    Tip,
    Important,
    Warning,
    Caution,
}

/// <summary>
/// What the five callouts are called, and how a block's kind is read.
///
/// Their colors used to be written here as well - a second copy of the ones in
/// <c>webshell/app.css</c>, for the Word export, held to the first by a build script. They are
/// the color theme's now: each kind has a bar, a fill and a title slot in every theme file,
/// which the preview and the Word export both read, so there is one copy and nothing to check.
/// The name stays because the kind and its label are still what everything here keys on.
/// </summary>
public static class CalloutColors
{
    /// <summary>The label the preview writes above the body, and Word writes in bold.</summary>
    public static string TitleOf(CalloutKind kind) => kind switch
    {
        CalloutKind.Tip => "Tip",
        CalloutKind.Important => "Important",
        CalloutKind.Warning => "Warning",
        CalloutKind.Caution => "Caution",
        _ => "Note",
    };

    /// <summary>
    /// The name Markdig gives the kind, as it appears after the exclamation mark in the
    /// source. Unknown text is a note, which is what the preview does with it too.
    /// </summary>
    public static CalloutKind Parse(string? kind) =>
        kind?.Trim().ToUpperInvariant() switch
        {
            "TIP" => CalloutKind.Tip,
            "IMPORTANT" => CalloutKind.Important,
            "WARNING" => CalloutKind.Warning,
            "CAUTION" => CalloutKind.Caution,
            _ => CalloutKind.Note,
        };
}
