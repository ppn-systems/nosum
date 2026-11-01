// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Dev.C2S;

/// <summary>Development only: pushes a level-up banner to the sender. It does not change the skill. Not handled in Production.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DevLevelUp : PacketBase<DevLevelUp>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => DevOpCode.DevLevelUp;

    /// <summary>The skill, a <c>SkillKind</c> value: 1 farming, 2 foraging, 3 mining, 4 combat, 5 fishing.</summary>
    [WireRange(1, 5)]
    [SerializeOrder(0)] public int Skill { get; set; } = 1;

    /// <summary>The level to show, 1 to 10.</summary>
    [WireRange(1, 10)]
    [SerializeOrder(1)] public int Level { get; set; } = 1;
}
