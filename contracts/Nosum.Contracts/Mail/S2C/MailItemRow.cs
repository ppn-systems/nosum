// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Mail.S2C;

/// <summary>One item line attached to a mail. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MailItemRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static MailItemRow Create() => new();

    /// <summary>The item.</summary>
    [SerializeOrder(0)] public int ItemId { get; set; }

    /// <summary>How many.</summary>
    [SerializeOrder(1)] public int Quantity { get; set; }
}
