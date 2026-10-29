// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.C2S;

/// <summary>Retired: password upgrade of a guest; use LinkExternalIdentity. Retired (docs/adr/ADR-069): the class and its opcode stay reserved and are never reused; it has no fields and the server has no handler.</summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UpgradeGuest : PacketBase<UpgradeGuest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.UpgradeGuest;
}
