// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Pet.C2S;

/// <summary>Adopts a cat or a dog. The name is player-authored: the server cleans it and masks profanity, as chat does (docs/adr/ADR-075 D3).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdoptPet : PacketBase<AdoptPet>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PetOpCode.AdoptPet;

    /// <summary>The kind: 1 cat, 2 dog.</summary>
    [SerializeOrder(0)] public byte Kind { get; set; }

    /// <summary>The name the farmer gives the pet.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(1)] public string Name { get; set; } = string.Empty;
}
