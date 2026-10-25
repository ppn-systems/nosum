// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Quest.S2C;

/// <summary>Reply to <see cref="C2S.GetQuests"/>: every quest the player has.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class QuestLog : PacketBase<QuestLog>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => QuestOpCode.QuestLog;

    /// <summary>The player's quests.</summary>
    [SerializeOrder(0)] public List<QuestRow> Quests { get; set; } = [];
}
