// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Shop.S2C;

/// <summary>Reply to every blacksmith request: the outcome, and the tool the smith holds for the player (if any) with the game days that matter.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BlacksmithState : PacketBase<BlacksmithState>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ShopOpCode.BlacksmithState;

    /// <summary>Outcome of the request.</summary>
    [SerializeOrder(0)] public BlacksmithStatus Status { get; set; }

    /// <summary>The item id of the upgraded tool the smith is making; zero when the smith holds nothing of the player's.</summary>
    [SerializeOrder(1)] public int ToolItemId { get; set; }

    /// <summary>The game day number the tool is ready; zero when there is none.</summary>
    [SerializeOrder(2)] public long ReadyDay { get; set; }

    /// <summary>The game day number now, so the client can show "ready in N days" without its own calendar.</summary>
    [SerializeOrder(3)] public long Today { get; set; }
}
