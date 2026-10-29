// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;
using Nosum.Contracts.Common;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Account.C2S;

/// <summary>
/// Signs the connection in with a Google or Microsoft ID token (docs/adr/ADR-069). The server verifies the token and signs in the account linked to
/// its subject; the first sign-in of an unknown subject creates a registered account.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ExternalSignIn : PacketBase<ExternalSignIn>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => AccountOpCode.ExternalSignIn;

    /// <summary>The provider that issued <see cref="IdToken"/>.</summary>
    [WireDefined]
    [SerializeOrder(0)] public ExternalProvider Provider { get; set; }

    /// <summary>The provider's ID token (a signed JWT). Bearer credential: never logged.</summary>
    [WireMaxLength(WireLimits.IdToken)]
    [SerializeOrder(1)] public string IdToken { get; set; } = string.Empty;

    /// <summary>Preferred language (the client's current UI language); <see cref="GameLanguage.Unspecified"/> means Vietnamese. Used only when the account is created.</summary>
    [SerializeOrder(2)] public GameLanguage PreferredLanguage { get; set; }
}
