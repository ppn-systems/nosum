// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Pet.C2S;

/// <summary>Asks for the player's pet.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetPet : PacketBase<GetPet>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PetOpCode.GetPet;
}
