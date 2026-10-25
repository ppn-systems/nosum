// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.C2S;

/// <summary>Opens a chest in the player's place: the server checks who the player is, the reach and the chest, then replies with its contents and keeps the player as a viewer until <see cref="CloseChest"/> (docs/adr/ADR-024 D4). Reply: <see cref="S2C.ChestSnapshot"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class OpenChest : PacketBase<OpenChest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.OpenChest;

    /// <summary>Id of the placed chest.</summary>
    [SerializeOrder(0)] public int ObjectId { get; set; }
}
