// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Building.C2S;

/// <summary>Asks the carpenter for an animal, living in one of the player's finished buildings (docs/adr/ADR-028 D3).</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class BuyAnimal : PacketBase<BuyAnimal>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => BuildingOpCode.BuyAnimal;

    /// <summary>The building the animal will live in.</summary>
    [SerializeOrder(0)] public Guid BuildingId { get; set; }

    /// <summary>The species (<c>AnimalKind</c> in Nosum.Gameplay).</summary>
    [SerializeOrder(1)] public byte Kind { get; set; }

    /// <summary>The name the player gives it; the nickname rules apply.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(2)] public string Name { get; set; } = string.Empty;
}
