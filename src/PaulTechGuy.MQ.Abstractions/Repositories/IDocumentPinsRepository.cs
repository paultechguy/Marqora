// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

namespace PaulTechGuy.MQ.Abstractions.Repositories;

/// <summary>
/// Persists which documents the user has pinned to the left of the tab strip. Never throws for
/// missing or corrupt storage.
///
/// Paths rather than identities, because a pin has to outlive the session that made it and a
/// document's <c>Id</c> does not. A file renamed or moved outside Marqora loses its pin, for want
/// of anything left to match; renamed *inside* Marqora it keeps it, because Save As moves the pin
/// across with the path.
///
/// The order the paths come back in means nothing. Where a pinned tab sits among the other pinned
/// tabs is the strip's business, and the strip records it in the session's document list.
/// </summary>
public interface IDocumentPinsRepository
{
    Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}
