// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Domain;

/// <summary>
/// A file as the workspace last saw it on disk.
///
/// Exists to tell a real change from a notification about nothing. A file watcher fires on
/// last-write time, name and size, so a touch, an antivirus scan or a build tool restamping a
/// file all produce events; comparing the stamp discards them before anyone is asked anything.
///
/// It also covers the gap in the older suppression window, which ignored watcher events for
/// two seconds after Marqora's own write. A slow network share can outrun that window; the
/// stamp does not care how long the write took.
///
/// Reading one is the workspace's job - this layer holds no file logic.
/// </summary>
public readonly record struct FileStamp(DateTimeOffset LastWriteUtc, long Length);
