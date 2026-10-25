// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Mail.S2C;

/// <summary>One mail. System mail is a code plus arguments, rendered by the client with its resources (docs/adr/ADR-010). A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MailRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static MailRow Create() => new();

    /// <summary>The mail id.</summary>
    [SerializeOrder(0)] public Guid Id { get; set; }

    /// <summary>The message code, for example <c>market.sold</c>.</summary>
    [SerializeOrder(1)] public string Code { get; set; } = string.Empty;

    /// <summary>The arguments of the code.</summary>
    [SerializeOrder(2)] public List<MailArgRow> Args { get; set; } = [];

    /// <summary>When the mail was sent, Unix milliseconds.</summary>
    [SerializeOrder(3)] public long SentAtMs { get; set; }

    /// <summary>When the mail expires, Unix milliseconds.</summary>
    [SerializeOrder(4)] public long ExpiresAtMs { get; set; }

    /// <summary>Whether the player read it.</summary>
    [SerializeOrder(5)] public bool Read { get; set; }

    /// <summary>Coins attached; 0 for none.</summary>
    [SerializeOrder(6)] public long Coins { get; set; }

    /// <summary>Items attached.</summary>
    [SerializeOrder(7)] public List<MailItemRow> Items { get; set; } = [];

    /// <summary>Whether the attachment was collected (always false when nothing is attached).</summary>
    [SerializeOrder(8)] public bool Collected { get; set; }
}
