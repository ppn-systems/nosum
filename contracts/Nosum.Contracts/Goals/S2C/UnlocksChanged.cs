// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Goals.S2C;

/// <summary>Pushed to the players of the maps an unlock changes (the Beach's bridge, docs/adr/ADR-037 D4): every unlock set so far, as <c>CommunityUnlockSet</c>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UnlocksChanged : PacketBase<UnlocksChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => GoalOpCode.UnlocksChanged;

    /// <summary>Gets or sets the unlocks that are set, as <c>CommunityUnlockSet</c>.</summary>
    [SerializeOrder(0)] public byte Unlocks { get; set; }
}
