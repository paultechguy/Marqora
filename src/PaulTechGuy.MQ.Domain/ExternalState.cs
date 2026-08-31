// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// How a document stands in relation to the file behind it.
///
/// One value drives everything downstream - the tab marker, the banner, whether the close
/// prompt appears - so the tab and the prompt cannot disagree about what happened.
/// </summary>
public enum ExternalState
{
    /// <summary>The buffer matches what was last read from, or written to, disk.</summary>
    InSync,

    /// <summary>
    /// The file was rewritten with content that genuinely differs, and the user has not
    /// resolved it yet. Only reached when the buffer is dirty or automatic reload is off;
    /// otherwise the document is simply reloaded.
    /// </summary>
    Changed,

    /// <summary>
    /// The file is gone from its path - deleted, moved, or its folder removed. The buffer is
    /// kept, and counts as unsaved so that saving writes the file back.
    /// </summary>
    Missing,
}
