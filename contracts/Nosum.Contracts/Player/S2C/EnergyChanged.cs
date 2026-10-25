// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Player.S2C;

/// <summary>Pushed to one player when their energy changes (tool use, eating, the daily refill) and once after joining (docs/adr/ADR-023 D5).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EnergyChanged : PacketBase<EnergyChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlayerOpCode.EnergyChanged;

    /// <summary>Energy now.</summary>
    [SerializeOrder(0)] public ushort Energy { get; set; }

    /// <summary>Largest energy.</summary>
    [SerializeOrder(1)] public ushort MaxEnergy { get; set; }
}
