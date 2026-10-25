// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Shop.C2S;

/// <summary>Collects the upgraded tool from the blacksmith. Reply: <see cref="S2C.BlacksmithState"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CollectTool : PacketBase<CollectTool>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ShopOpCode.CollectTool;
}
