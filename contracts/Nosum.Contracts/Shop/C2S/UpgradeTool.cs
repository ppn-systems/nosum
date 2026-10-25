// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Shop.C2S;

/// <summary>
/// Hands a tool to the blacksmith (docs/adr/ADR-026 D2). The server prices the upgrade, checks the player stands at the counter and has the tool, the
/// coins and the bars, and moves them in one transaction; the tool is held for two game days.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UpgradeTool : PacketBase<UpgradeTool>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ShopOpCode.UpgradeTool;

    /// <summary>The item id of the tool to upgrade (for example the basic hoe).</summary>
    [SerializeOrder(0)] public int ItemId { get; set; }
}
