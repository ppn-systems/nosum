// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Node.S2C;

/// <summary>One resource node. <see cref="Kind"/>, <see cref="Variant"/> and <see cref="Stage"/> are the values of <c>NodeKind</c> and the tree enums in Nosum.Gameplay.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class NodeRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static NodeRow Create() => new();

    /// <summary>Node id, unique across the game and never reused.</summary>
    [SerializeOrder(0)] public int Id { get; set; }

    /// <summary>Tile x.</summary>
    [SerializeOrder(1)] public ushort X { get; set; }

    /// <summary>Tile y.</summary>
    [SerializeOrder(2)] public ushort Y { get; set; }

    /// <summary>What the node is (<c>NodeKind</c>).</summary>
    [SerializeOrder(3)] public byte Kind { get; set; }

    /// <summary>For trees and stumps: 0 oak, 1 maple, 2 pine.</summary>
    [SerializeOrder(4)] public byte Variant { get; set; }

    /// <summary>For trees: 0 seed, 1 sapling, 2 bush, 3 grown.</summary>
    [SerializeOrder(5)] public byte Stage { get; set; }

    /// <summary>Hits left before the node breaks; the client shakes the node when it drops.</summary>
    [SerializeOrder(6)] public byte Hits { get; set; }
}
