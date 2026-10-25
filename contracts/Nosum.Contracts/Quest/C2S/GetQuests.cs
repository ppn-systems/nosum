// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Quest.C2S;

/// <summary>Asks for the signed-in player's quests. A new player is given the tutorial quest on the first request.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetQuests : PacketBase<GetQuests>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => QuestOpCode.GetQuests;

    /// <summary>Reserved; the request carries no data.</summary>
    [SerializeOrder(0)] public byte Reserved { get; set; }
}
