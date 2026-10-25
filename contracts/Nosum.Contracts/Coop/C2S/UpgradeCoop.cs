// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Coop.C2S;

/// <summary>Buys the next level of an upgrade track from the warehouse stock (founder only).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UpgradeCoop : PacketBase<UpgradeCoop>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CoopOpCode.UpgradeCoop;

    /// <summary>The track to upgrade.</summary>
    [SerializeOrder(0)] public CoopUpgradeTrack Track { get; set; }

    /// <summary>The level to buy. It must be exactly one above the current level, so a request sent twice pays once.</summary>
    [SerializeOrder(1)] public byte TargetLevel { get; set; }
}
