// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.World;

/// <summary>
/// Opcodes of the World domain (the map a player is on and changes of it).
/// <para>Band <c>0x7000-0x70FF</c> is reserved for this domain. C2S requests use <c>0x7000-0x703F</c>
/// (none yet: the server changes maps by itself when a player walks onto an exit),
/// S2C pushes use <c>0x7040-0x707F</c>. Never reassign or reuse a value.</para>
/// <para>Sub-range <c>0x7300-0x73FF</c> belongs to chunks (docs/adr/ADR-022 D7): C2S <c>0x7300-0x733F</c> (none yet), S2C <c>0x7340-0x737F</c>.</para>
/// </summary>
public static class WorldOpCode
{
    // -- S2C (0x7040-0x707F) --------------------------------------------------

    /// <summary>Push to one player: the server moved the player to another map.</summary>
    public const ushort MapChanged = 0x7040;

    // -- Chunks: S2C (0x7340-0x737F) -------------------------------------------

    /// <summary>
    /// Push to one player: a chunk joined the player's interest set; this is everything the server keeps in it
    /// (farm plots, placed objects), sent once, deltas follow.
    /// </summary>
    public const ushort ChunkState = 0x7340;

    /// <summary>Push to one player: a chunk left the player's interest set; the client drops its mirror of it.</summary>
    public const ushort ChunkLeft = 0x7341;
}
