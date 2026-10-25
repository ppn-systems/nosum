// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Chat;

/// <summary>Where a chat message goes (docs/adr/ADR-029 D1). Values are append-only.</summary>
public enum ChatChannel : byte
{
    /// <summary>The players near the sender (their chunk interest set, docs/adr/ADR-022; the whole instance until chunks land).</summary>
    Local = 0,
    /// <summary>Everybody in the sender's map instance.</summary>
    Map = 1,
    /// <summary>The online members of the sender's co-op.</summary>
    Coop = 2,
    /// <summary>One online player, addressed by nickname.</summary>
    Whisper = 3,
}
