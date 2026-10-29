// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Serialization;
using Nosum.Contracts.Validation;

namespace Nosum.Contracts.Look.S2C;

/// <summary>One player's look as ids only (docs/adr/ADR-006 D6). A plain serializable row: no opcode, no header.</summary>
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class LookRow
{
    /// <summary>Creates an empty row (required by the serializer).</summary>
    public static LookRow Create() => new();

    /// <summary>The player's id on the map.</summary>
    [SerializeOrder(0)] public uint PlayerId { get; set; }

    /// <summary>Skin tone id.</summary>
    [SerializeOrder(1)] public byte SkinId { get; set; }

    /// <summary>Hair style id.</summary>
    [SerializeOrder(2)] public byte HairStyleId { get; set; }

    /// <summary>Hair dye id.</summary>
    [SerializeOrder(3)] public byte HairDyeId { get; set; }

    /// <summary>Worn shirt item id; zero for the plain shirt.</summary>
    [SerializeOrder(4)] public int ShirtItemId { get; set; }

    /// <summary>Worn trousers item id; zero for the plain trousers.</summary>
    [SerializeOrder(5)] public int PantsItemId { get; set; }

    /// <summary>Worn hat item id; zero for none.</summary>
    [SerializeOrder(6)] public int HatItemId { get; set; }

    /// <summary>The player's nickname, cached by the client under <see cref="PlayerId"/> (sent with the look: on join, map change and every change, never in a snapshot). Appended in protocol version 3.</summary>
    [SerializeOrder(7)] public string Nickname { get; set; } = string.Empty;

    /// <summary>The kind of the player's pet: 0 none, 1 cat, 2 dog (docs/adr/ADR-071 D3). Appended in protocol version 13.</summary>
    [SerializeOrder(8)] public byte PetKind { get; set; }

    /// <summary>The pet's name, cleaned and masked by the server; empty without a pet. Appended in protocol version 13.</summary>
    [WireMaxLength(WireLimits.ShortText)]
    [SerializeOrder(9)] public string PetName { get; set; } = string.Empty;
}
