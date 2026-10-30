// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Pet.C2S;

/// <summary>Pets the player's pet: once a game day, for a mood and energy bonus.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PetPet : PacketBase<PetPet>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PetOpCode.PetPet;
}
