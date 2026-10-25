// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.S2C;

/// <summary>Pushed to one player when a skill reaches a new level (the client shows a toast).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class LevelUp : PacketBase<LevelUp>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.LevelUp;

    /// <summary>The skill: 1 farming, 2 foraging, 3 mining.</summary>
    [SerializeOrder(0)] public byte Skill { get; set; }

    /// <summary>The level reached, 1 to 10.</summary>
    [SerializeOrder(1)] public byte Level { get; set; }
}
