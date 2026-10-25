// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Nosum.Contracts.Account.S2C;

/// <summary>
/// Pushed to a guest when the game unlocks the invitation to register (the first quest was finished),
/// so the client can offer it without signing in again.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RegistrationPromptUnlocked : PacketBase<RegistrationPromptUnlocked>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.RegistrationPromptUnlocked;

    /// <summary>Reserved; the push carries no data.</summary>
    [SerializeOrder(0)] public byte Reserved { get; set; }
}
