// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Quest.S2C;

/// <summary>Pushed to one player when a quest's progress changed or it was completed.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class QuestUpdate : PacketBase<QuestUpdate>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => QuestOpCode.QuestUpdate;

    /// <summary>The quest in its new state.</summary>
    [SerializeOrder(0)] public QuestRow Quest { get; set; } = new();
}
