// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.World.S2C;

/// <summary>
/// Pushed to one player when the server moved them to another map, after they walked onto an exit.
/// The server decides when and where; the client loads the map and shows the new place. Sent before
/// the player becomes visible on the new map, so no snapshot of it arrives first. Ids and numbers only.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapChanged : PacketBase<MapChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => WorldOpCode.MapChanged;

    /// <summary>Id of the player on this server; it stays the same across map changes while the player is connected.</summary>
    [SerializeOrder(0)] public uint PlayerId { get; set; }

    /// <summary>Stable id of the map the player is now on, for example <c>farmhouse</c>.</summary>
    [SerializeOrder(1)] public string MapId { get; set; } = string.Empty;

    /// <summary>Feet x on the new map, world pixels.</summary>
    [SerializeOrder(2)] public float X { get; set; }

    /// <summary>Feet y on the new map, world pixels.</summary>
    [SerializeOrder(3)] public float Y { get; set; }

    /// <summary>Facing: 0 down, 1 right, 2 up, 3 left.</summary>
    [SerializeOrder(4)] public byte Facing { get; set; }
}
