// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Domain.Looks;

/// <summary>A player's persisted look: avatar part ids and the clothing items worn (docs/adr/ADR-014). Ids only; worn items are out of the inventory.</summary>
public sealed class PlayerAppearance
{
    /// <summary>Gets or sets the owning player; also this row's primary key.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Gets or sets the skin tone id.</summary>
    public int SkinId { get; set; }

    /// <summary>Gets or sets the hair style id.</summary>
    public int HairStyleId { get; set; }

    /// <summary>Gets or sets the hair dye id.</summary>
    public int HairDyeId { get; set; }

    /// <summary>Gets or sets the worn shirt item id; zero for the plain shirt.</summary>
    public int ShirtItemId { get; set; }

    /// <summary>Gets or sets the worn trousers item id; zero for the plain trousers.</summary>
    public int PantsItemId { get; set; }

    /// <summary>Gets or sets the worn hat item id; zero for none.</summary>
    public int HatItemId { get; set; }

    /// <summary>Gets or sets when the look last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
