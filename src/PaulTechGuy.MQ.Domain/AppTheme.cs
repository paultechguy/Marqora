// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>Theme the user has chosen, before it is resolved against the OS setting.</summary>
public enum AppTheme
{
    /// <summary>Follow the Windows app-mode setting and track changes live.</summary>
    System = 0,
    Light = 1,
    Dark = 2,
}
