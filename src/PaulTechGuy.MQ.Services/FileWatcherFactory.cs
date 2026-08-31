// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Services;

namespace PaulTechGuy.MQ.Services;

/// <summary>
/// Builds a watcher per open document.
///
/// A watcher follows one file, and the workspace holds several at once, so watchers cannot
/// be singletons. Taking the logger factory rather than a logger keeps each watcher's log
/// entries attributed to FileWatcher rather than to this class.
/// </summary>
public sealed class FileWatcherFactory(ILoggerFactory loggerFactory) : IFileWatcherFactory
{
    public IFileWatcher Create() => new FileWatcher(loggerFactory.CreateLogger<FileWatcher>());
}
