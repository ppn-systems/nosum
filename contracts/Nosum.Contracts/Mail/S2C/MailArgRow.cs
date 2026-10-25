// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Mail.S2C;

/// <summary>One argument of a system mail (an item id, a quantity, a nickname...). A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MailArgRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static MailArgRow Create() => new();

    /// <summary>The argument as text; what it means depends on the mail code.</summary>
    [SerializeOrder(0)] public string Value { get; set; } = string.Empty;
}
