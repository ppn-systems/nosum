// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Reply to <see cref="C2S.GetRecipeBook"/>: the recipes the player can craft now. Every other recipe is locked.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RecipeBook : PacketBase<RecipeBook>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.RecipeBook;

    /// <summary>Ids of the unlocked recipes.</summary>
    [SerializeOrder(0)] public List<int> Unlocked { get; set; } = [];
}
