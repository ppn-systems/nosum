// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Journal.S2C;

/// <summary>
/// The player's collection journal: one row per item the player has obtained. Items never obtained are not sent; the client draws them as silhouettes.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class JournalReply : PacketBase<JournalReply>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => JournalOpCode.JournalReply;

    /// <summary>The discovered entries, lowest item id first.</summary>
    [SerializeOrder(0)] public List<JournalEntry> Entries { get; set; } = [];
}
