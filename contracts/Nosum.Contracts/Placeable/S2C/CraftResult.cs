// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Placeable.S2C;

/// <summary>Reply to <see cref="C2S.Craft"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CraftResult : PacketBase<CraftResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => PlaceableOpCode.CraftResult;

    /// <summary>Outcome code.</summary>
    [SerializeOrder(0)] public CraftStatus Status { get; set; }

    /// <summary>The recipe asked for.</summary>
    [SerializeOrder(1)] public ushort RecipeId { get; set; }

    /// <summary>How many crafts were made; zero on a refusal.</summary>
    [SerializeOrder(2)] public byte Times { get; set; }
}
