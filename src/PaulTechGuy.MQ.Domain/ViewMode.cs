// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>Which pane(s) the shell shows.</summary>
public enum ViewMode
{
    /// <summary>Markdown source only.</summary>
    Source = 0,

    /// <summary>Rendered preview only.</summary>
    Preview = 1,

    /// <summary>Source and preview together, with optional scroll synchronization.</summary>
    SideBySide = 2,
}
