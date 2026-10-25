// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Placeable.C2S;

/// <summary>Moves a stack, or part of it, between the player's inventory and an open chest, or inside the chest. One atomic, audited step; the server decides everything from what the two slots hold. Reply: <see cref="S2C.ChestResult"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ChestMove : PacketBase<ChestMove>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.ChestMove;

    /// <summary>Id of the open chest.</summary>
    [WireRange(0, int.MaxValue)]
    [SerializeOrder(0)] public int ObjectId { get; set; }

    /// <summary>Where the stack is now.</summary>
    [WireDefined]
    [SerializeOrder(1)] public ChestSide FromSide { get; set; }

    /// <summary>Slot it is in.</summary>
    [SerializeOrder(2)] public byte FromSlot { get; set; }

    /// <summary>Where it goes.</summary>
    [WireDefined]
    [SerializeOrder(3)] public ChestSide ToSide { get; set; }

    /// <summary>Slot it goes to, or 255 for the first slot that fits.</summary>
    [SerializeOrder(4)] public byte ToSlot { get; set; }

    /// <summary>How many to move; zero means the whole stack.</summary>
    [SerializeOrder(5)] public ushort Quantity { get; set; }
}
