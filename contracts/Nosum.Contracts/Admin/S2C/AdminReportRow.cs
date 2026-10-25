// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Admin.S2C;

/// <summary>One chat report in the list. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class AdminReportRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static AdminReportRow Create() => new();

    /// <summary>The report id.</summary>
    [SerializeOrder(0)] public Guid ReportId { get; set; }

    /// <summary>The nickname of the player who reported.</summary>
    [SerializeOrder(1)] public string Reporter { get; set; } = string.Empty;

    /// <summary>The nickname of the player who was reported.</summary>
    [SerializeOrder(2)] public string Reported { get; set; } = string.Empty;

    /// <summary>When the report was made, Unix milliseconds on the server clock.</summary>
    [SerializeOrder(3)] public long CreatedAtMs { get; set; }
}
