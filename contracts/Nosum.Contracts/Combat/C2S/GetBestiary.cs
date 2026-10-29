// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Combat.C2S;

/// <summary>Asks for the player's kill counts per species and the equipped slayer title (docs/adr/ADR-067). Reply: <see cref="S2C.BestiarySnapshot"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetBestiary : PacketBase<GetBestiary>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => CombatOpCode.GetBestiary;
}
