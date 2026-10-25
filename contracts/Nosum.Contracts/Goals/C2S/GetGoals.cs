// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Goals.C2S;

/// <summary>Asks for the goals of the current season.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetGoals : PacketBase<GetGoals>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => GoalOpCode.GetGoals;
}
