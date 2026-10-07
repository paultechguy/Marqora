// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>What a face is for on paper. A paper element names a role, never a font.</summary>
public enum PaperFaceRole
{
    /// <summary>Body text, tables, lists, footnotes, the running header and footer.</summary>
    Text,

    /// <summary>Headings and the cover title.</summary>
    Display,

    /// <summary>Code blocks and inline code.</summary>
    Code,

    /// <summary>The labels inside diagrams.</summary>
    Diagram,
}

/// <summary>
/// One face, as each export names it.
///
/// Two names because the two exports ask for a face differently. Word has only bold on or off,
/// so a semibold is a family of its own there ("Segoe UI Semibold"); CSS has weights, so the
/// same semibold is a family chain and the number 600. <see cref="WordFamily"/> is null for a
/// role Word never draws text in - diagram labels reach Word inside a picture.
/// </summary>
public sealed record PaperFace(PaperFaceRole Role, string? WordFamily, string CssFamilies, int CssWeight);

/// <summary>
/// The faces on paper, and the only place they are named
/// (docs/Export-Alignment-Plan.md, §8, Faces; docs/Paper-Design.md).
///
/// Changing a face is a one-row edit here: Word's theme fonts and code font, the print
/// stylesheet, the paged print page and every printout follow it. A test fails if a font name
/// is written anywhere else in the export code, which is what keeps it one place after the
/// first change.
///
/// Segoe UI rather than Aptos: Aptos is an Office cloud font, installed where WebView2 cannot
/// see it, so a spec naming it gave Word Aptos and the PDF Segoe UI - two looks under one spec.
/// Both engines can see every face below on every supported Windows.
/// </summary>
public static class PaperFaces
{
    public static PaperFace Text { get; } = new(
        PaperFaceRole.Text,
        "Segoe UI",
        "\"Segoe UI Variable Text\", \"Segoe UI\", sans-serif",
        400);

    public static PaperFace Display { get; } = new(
        PaperFaceRole.Display,
        "Segoe UI Semibold",
        "\"Segoe UI Variable Display\", \"Segoe UI\", sans-serif",
        600);

    public static PaperFace Code { get; } = new(
        PaperFaceRole.Code,
        "Cascadia Mono",
        "\"Cascadia Mono\", Consolas, monospace",
        400);

    public static PaperFace Diagram { get; } = new(
        PaperFaceRole.Diagram,
        null,
        "\"Segoe UI\", sans-serif",
        400);

    public static IReadOnlyList<PaperFace> All { get; } = [Text, Display, Code, Diagram];

    public static PaperFace For(PaperFaceRole role) => role switch
    {
        PaperFaceRole.Text => Text,
        PaperFaceRole.Display => Display,
        PaperFaceRole.Code => Code,
        PaperFaceRole.Diagram => Diagram,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    /// <summary>The custom property a stylesheet reads a role's families from.</summary>
    public static string CssProperty(PaperFaceRole role) => "--mq-face-" + role.ToString().ToLowerInvariant();
}
