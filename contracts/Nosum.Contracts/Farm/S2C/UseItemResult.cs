// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Farm.S2C;

/// <summary>Reply to <see cref="C2S.UseItem"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UseItemResult : PacketBase<UseItemResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => FarmOpCode.UseItemResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public FarmStatus Status { get; set; }

    /// <summary>What was done; <see cref="FarmAction.None"/> unless the status is <see cref="FarmStatus.Ok"/>.</summary>
    [SerializeOrder(1)] public FarmAction Action { get; set; }

    /// <summary>
    /// How many tiles the use worked: one for a single-tile use that worked, and for a wide use (<see cref="C2S.UseItemArea"/>) the tiles of the line
    /// that worked, each of which was charged its own energy (docs/adr/ADR-088). Zero on a refusal.
    /// </summary>
    [SerializeOrder(2)] public byte Worked { get; set; }

    /// <summary>
    /// Watered game days the unripe crop still needs, set with <see cref="FarmStatus.NotMature"/> and zero otherwise (docs/adr/ADR-097 D2). The client
    /// writes the reason with this number; no text crosses the wire.
    /// </summary>
    [SerializeOrder(3)] public byte DaysLeft { get; set; }
}
