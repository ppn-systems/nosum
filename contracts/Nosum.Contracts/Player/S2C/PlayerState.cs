// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Player.S2C;

/// <summary>One player inside a <see cref="MapSnapshot"/>. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlayerState
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static PlayerState Create() => new();

    /// <summary>Id of the player.</summary>
    [SerializeOrder(0)] public uint PlayerId { get; set; }

    /// <summary>Feet x in world pixels.</summary>
    [SerializeOrder(1)] public float X { get; set; }

    /// <summary>Feet y in world pixels.</summary>
    [SerializeOrder(2)] public float Y { get; set; }

    /// <summary>Facing: 0 down, 1 right, 2 up, 3 left.</summary>
    [SerializeOrder(3)] public byte Facing { get; set; }

    /// <summary>Whether the player is walking.</summary>
    [SerializeOrder(4)] public bool Moving { get; set; }

    /// <summary>Item in the hotbar slot the player holds, drawn in hand; zero for empty hands or when the server does not hold the bag in memory.</summary>
    [SerializeOrder(5)] public int HeldItemId { get; set; }
}
