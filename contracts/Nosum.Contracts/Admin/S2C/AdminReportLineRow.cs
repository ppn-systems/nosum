// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Admin.S2C;

/// <summary>One stored message of a chat report. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdminReportLineRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static AdminReportLineRow Create() => new();

    /// <summary>The channel (a <c>ChatChannel</c> value).</summary>
    [SerializeOrder(0)] public byte Channel { get; set; }

    /// <summary>The sender's nickname when the message was sent.</summary>
    [SerializeOrder(1)] public string Sender { get; set; } = string.Empty;

    /// <summary>The text as it was delivered (cleaned and masked).</summary>
    [SerializeOrder(2)] public string Text { get; set; } = string.Empty;

    /// <summary>When the message was sent, Unix milliseconds on the server clock.</summary>
    [SerializeOrder(3)] public long SentAtMs { get; set; }
}
