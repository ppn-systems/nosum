// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.S2C;

/// <summary>
/// Push when an ordinary monster winds up a melee strike: the monster is shown raising and flashing until the strike lands after
/// <see cref="DelayMs"/>. The strike hits only if the player is still in reach then (docs/adr/ADR-092).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class StrikeTelegraph : PacketBase<StrikeTelegraph>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.StrikeTelegraph;

    /// <summary>Monster id within the map/floor (the same id as <c>MonsterChanged.MonsterId</c>).</summary>
    [SerializeOrder(0)] public int MonsterId { get; set; }

    /// <summary>Milliseconds from this push until the strike lands.</summary>
    [SerializeOrder(1)] public ushort DelayMs { get; set; }
}
