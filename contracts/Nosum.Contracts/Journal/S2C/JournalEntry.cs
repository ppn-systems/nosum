// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;

namespace Nosum.Contracts.Journal.S2C;

/// <summary>One discovered item of the collection journal. A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class JournalEntry
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static JournalEntry Create() => new();

    /// <summary>The item id from the item catalogue.</summary>
    [SerializeOrder(0)] public int ItemId { get; set; }

    /// <summary>How many of the item the player has obtained in total.</summary>
    [SerializeOrder(1)] public int Count { get; set; }

    /// <summary>When the player first obtained the item, in Unix seconds (UTC).</summary>
    [SerializeOrder(2)] public long FirstObtainedUnix { get; set; }
}
