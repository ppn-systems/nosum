// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Mine;

/// <summary>
/// Opcodes of the Mine domain: the elevator and the player's deepest floor (docs/adr/ADR-026 D3).
/// <para>Band <c>0xC100-0xC1FF</c> is reserved for this domain, next to the Shop band. C2S requests use the lower half, S2C replies the upper half. Going down a ladder is a tool use on the ladder's tile (<c>UseItem</c>), not a packet of its own. Never reassign or reuse a value.</para>
/// </summary>
public static class MineOpCode
{
    // -- C2S --

    /// <summary>Asks for the deepest floor the player reached and the one they are on. Reply: <see cref="S2C.MineState"/>.</summary>
    public const ushort GetMine = 0xC100;

    /// <summary>Rides the elevator to the entrance (floor 0) or to an elevator floor the player has reached. Reply: <see cref="S2C.MineState"/>; the map change arrives as <c>MapChanged</c>.</summary>
    public const ushort RideElevator = 0xC101;

    // -- S2C --

    /// <summary>Reply to <see cref="GetMine"/> and <see cref="RideElevator"/>.</summary>
    public const ushort MineState = 0xC140;
}
