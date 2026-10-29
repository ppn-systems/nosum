// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Journal.C2S;

/// <summary>Asks for the player's collection journal. Reply: <see cref="S2C.JournalReply"/>.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class GetJournal : PacketBase<GetJournal>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => JournalOpCode.GetJournal;
}
