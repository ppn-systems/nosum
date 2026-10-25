// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.World;

/// <summary>
/// Opcodes of the World domain (the map a player is on and changes of it).
/// <para>Band <c>0x7000-0x70FF</c> is reserved for this domain. C2S requests use <c>0x7000-0x703F</c>
/// (none yet: the server changes maps by itself when a player walks onto an exit),
/// S2C pushes use <c>0x7040-0x707F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class WorldOpCode
{
    // -- S2C (0x7040-0x707F) --------------------------------------------------

    /// <summary>Push to one player: the server moved the player to another map.</summary>
    public const ushort MapChanged = 0x7040;
}
