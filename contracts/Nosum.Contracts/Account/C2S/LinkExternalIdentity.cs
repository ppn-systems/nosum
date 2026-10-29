// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Account.C2S;

/// <summary>
/// Turns the signed-in guest into a registered player by linking a Google or Microsoft identity (docs/adr/ADR-069). Only valid on a guest
/// session; the player id and data stay, and the guest's session token is replaced.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class LinkExternalIdentity : PacketBase<LinkExternalIdentity>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.LinkExternalIdentity;

    /// <summary>The provider that issued <see cref="IdToken"/>.</summary>
    [WireDefined]
    [SerializeOrder(0)] public ExternalProvider Provider { get; set; }

    /// <summary>The provider's ID token (a signed JWT). Bearer credential: never logged.</summary>
    [WireMaxLength(WireLimits.IdToken)]
    [SerializeOrder(1)] public string IdToken { get; set; } = string.Empty;

    /// <summary>Wanted display nickname, replacing the guest's neutral one (3-16 characters).</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(2)] public string Nickname { get; set; } = string.Empty;
}
