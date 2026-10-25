// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Node;

/// <summary>
/// Opcodes of the resource-node domain: trees, stones, weeds and the other things a player clears with an axe, pickaxe or scythe
/// (docs/adr/ADR-021). Tools are used with <see cref="Farm.C2S.UseItem"/>; this domain only mirrors the nodes to the client.
/// <para>Sub-range <c>0x7100-0x71FF</c> of the World band. C2S requests use <c>0x7100-0x713F</c>, S2C replies and pushes use
/// <c>0x7140-0x717F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class NodeOpCode
{
    // -- C2S (0x7100-0x713F) --------------------------------------------------

    /// <summary>Asks for every resource node of the instance the player is in. Reply: <see cref="S2C.NodeSnapshot"/>.</summary>
    public const ushort GetNodes = 0x7100;

    // -- S2C (0x7140-0x717F) --------------------------------------------------

    /// <summary>Reply to <see cref="GetNodes"/>.</summary>
    public const ushort NodeSnapshot = 0x7140;

    /// <summary>Push to the players of an instance: nodes were added or changed, or removed.</summary>
    public const ushort NodeChanged = 0x7141;

    /// <summary>Push to one player: how much water the watering can holds now.</summary>
    public const ushort ToolState = 0x7142;

    /// <summary>Push to the players of an instance: tilled tiles were cleared by a pickaxe.</summary>
    public const ushort FarmTilesCleared = 0x7143;
}
