// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// How loudly a diagnostic should speak. These map onto Monaco's marker severities, but
/// deliberately stop short of Error: nothing the analyzer finds stops a document rendering.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Barely there. Style rules the formatter would fix on request.</summary>
    Hint = 0,

    /// <summary>Worth knowing about, but not wrong.</summary>
    Information = 1,

    /// <summary>Something is broken: a link that leads nowhere, an image that is missing.</summary>
    Warning = 2,
}

/// <summary>
/// Something the analyzer noticed, and where.
///
/// Positions are zero-based, like everything else inside the app; Monaco counts from one and
/// the conversion happens once, at the bridge.
/// </summary>
public sealed record Diagnostic
{
    public required int Line { get; init; }

    public required int Column { get; init; }

    /// <summary>Exclusive end column, so a marker can underline the offending run.</summary>
    public required int EndColumn { get; init; }

    public required DiagnosticSeverity Severity { get; init; }

    /// <summary>Stable identifier for the rule, shown beside the message.</summary>
    public required string Rule { get; init; }

    public required string Message { get; init; }
}
