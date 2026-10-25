// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.C2S;

/// <summary>Crafts a recipe one or more times: the server checks the unlock and the items, then removes the inputs and adds the outputs in one atomic step (docs/adr/ADR-024 D1). Reply: <see cref="S2C.CraftResult"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class Craft : PacketBase<Craft>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.Craft;

    /// <summary>Recipe id from the recipe catalogue.</summary>
    [SerializeOrder(0)] public ushort RecipeId { get; set; }

    /// <summary>How many crafts, 1 to 99.</summary>
    [SerializeOrder(1)] public byte Times { get; set; }
}
