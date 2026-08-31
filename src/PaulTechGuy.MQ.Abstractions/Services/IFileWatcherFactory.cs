// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Services;

/// <summary>
/// Creates a watcher per open document.
///
/// A watcher follows exactly one file, and the workspace now holds several at once, so the
/// watcher can no longer be a singleton. The factory keeps that construction detail out of
/// the workspace.
/// </summary>
public interface IFileWatcherFactory
{
    IFileWatcher Create();
}
