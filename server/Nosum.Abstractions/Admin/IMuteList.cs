// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Abstractions.Admin;

/// <summary>
/// Who may not chat right now (docs/adr/ADR-031 D7). In memory for the one server process and loaded from the database at start, so chat
/// checks it on every message without a query.
/// </summary>
public interface IMuteList
{
    /// <summary>Tells whether a player is muted.</summary>
    /// <param name="playerId">The player.</param>
    /// <returns><see langword="true"/> while the mute lasts.</returns>
    bool IsMuted(Guid playerId);
}
