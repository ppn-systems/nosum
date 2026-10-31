// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Pet.C2S;

/// <summary>Gives the player's pet back to the carpenter for half its price (docs/adr/ADR-075 amendment). The pet is removed.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RehomePet : PacketBase<RehomePet>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PetOpCode.RehomePet;
}
