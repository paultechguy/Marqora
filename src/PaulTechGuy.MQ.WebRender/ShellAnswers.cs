// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.Domain;

namespace PaulTechGuy.MQ.WebRender;

/// <summary>
/// What the shell's export answers mean, read once for both hosts: a diagram's picture, an
/// equation's, and what a PDF lost. The bridge carries the answers as the strings the shell
/// sends; these turn them into what an export uses.
/// </summary>
public static class ShellAnswers
{
    /// <summary>A diagram picture from its base64, or null when it is not readable.</summary>
    public static byte[]? DiagramPng(string data, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            return Convert.FromBase64String(data);
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "The shell's diagram image was not readable base64.");
            return null;
        }
    }

    /// <summary>An equation picture from "depth|base64" (see mathPngOf in app.js), or null when it is not readable.</summary>
    public static MathPicture? MathPng(string data, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(logger);

        int bar = data.IndexOf('|', StringComparison.Ordinal);

        try
        {
            double depth = bar > 0 && double.TryParse(data.AsSpan(0, bar), NumberStyles.Float, CultureInfo.InvariantCulture, out double read) ? read : 0;

            return new MathPicture(Convert.FromBase64String(data[(bar + 1)..]), depth);
        }
        catch (FormatException ex)
        {
            logger.LogWarning(ex, "The shell's equation image was not readable base64.");
            return null;
        }
    }

    /// <summary>
    /// What a PDF lost that its document asked for: a row for every diagram the print markup
    /// lists as failing to draw, and one for footnotes the paged engine had to make endnotes.
    /// </summary>
    public static IReadOnlyList<ExportIssue> PdfIssues(string? markup, PdfJob job, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(logger);

        IReadOnlyList<ExportIssue> issues = DiagramIssues(markup, logger);

        // Plan O4: every note is still in the PDF, only at the end rather than at its page foot,
        // so the row is worth knowing rather than a loss - and it says why, because a reader
        // who expected footnotes will otherwise think the export got them wrong.
        if (job.NotesAsEndnotes > 0)
        {
            issues =
            [
                new ExportIssue
                {
                    Line = 0,
                    IsAdvisory = true,
                    Problem = "The footnotes are endnotes in this PDF: one could not be placed at the foot of its page, and a document takes one kind or the other",
                    Item = job.NotesAsEndnotes == 1 ? "1 footnote" : $"{job.NotesAsEndnotes} footnotes",
                },
                .. issues,
            ];
        }

        return issues;
    }

    /// <summary>
    /// A report row for every diagram the shell listed as failing to draw. The PDF carries the
    /// diagram's error where the drawing would be, so the row says that rather than Word's
    /// "its source is in the document instead".
    /// </summary>
    private static IReadOnlyList<ExportIssue> DiagramIssues(string? markup, ILogger logger)
    {
        if (string.IsNullOrEmpty(markup))
        {
            return [];
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(markup);

            if (!parsed.RootElement.TryGetProperty("diagramErrors", out JsonElement errors)
                || errors.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return
            [
                .. errors.EnumerateArray().Select(error => new ExportIssue
                {
                    // Markdig counts from zero and the editor from one; -1 is "no line known".
                    Line = ReadInt(error, "line", -1) is int line and >= 0 ? line + 1 : 0,
                    Problem = "The diagram could not be drawn; the PDF shows its error instead",
                    Item = OneLine(ReadString(error, "message")) is { Length: > 0 } message
                        ? (message.Length <= 80 ? message : message[..80].TrimEnd() + "…")
                        : "A diagram",
                }),
            ];
        }
        catch (JsonException ex)
        {
            logger.LogDebug(ex, "The print markup's diagram list could not be read.");
            return [];
        }
    }

    /// <summary>
    /// A message on one line. Mermaid's parse errors run over three - the message, the source
    /// around the fault, a row of dashes pointing at it - and a report row is read in a column.
    /// </summary>
    private static string OneLine(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;

    private static int ReadInt(JsonElement payload, string name, int fallback) =>
        payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty(name, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int result)
                ? result
                : fallback;
}
