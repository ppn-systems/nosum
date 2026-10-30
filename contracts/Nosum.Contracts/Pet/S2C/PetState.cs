// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Pet.S2C;

/// <summary>Reply to every pet request: the outcome and the pet as it stands. Kind 0 means no pet.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PetState : PacketBase<PetState>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PetOpCode.PetState;

    /// <summary>The outcome.</summary>
    [SerializeOrder(0)] public PetStatus Status { get; set; }

    /// <summary>The kind: 0 none, 1 cat, 2 dog.</summary>
    [SerializeOrder(1)] public byte Kind { get; set; }

    /// <summary>The pet's name; empty when there is no pet.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(2)] public string Name { get; set; } = string.Empty;

    /// <summary>The pet's mood, 0 to 100.</summary>
    [SerializeOrder(3)] public int Mood { get; set; }

    /// <summary>Whether the pet was already petted today.</summary>
    [SerializeOrder(4)] public bool PettedToday { get; set; }
}
