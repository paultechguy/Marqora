// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Cli.Reporting;

/// <summary>How much a finding matters. Only errors change the exit code, and hints only under --strict.</summary>
internal enum FindingSeverity
{
    Error,
    Warning,
    Info,
    Hint,
}

/// <summary>
/// One thing a verb found in a document: a dead link, an uncertain heading, a list it left alone.
/// Line and column count from one; zero means the finding is about the whole document.
/// </summary>
/// <param name="Code">A short stable name, so a script can filter on it without reading prose.</param>
internal sealed record DocumentFinding(int Line, int Column, string Code, FindingSeverity Severity, string Message);
