// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Contracts.Placeable;

/// <summary>
/// Opcodes of the Placeable domain: objects players put on the ground of their own place (furniture in the farmhouse now;
/// decorations and machines later) (docs/adr/ADR-015).
/// <para>Band <c>0xB000-0xB0FF</c> is reserved for this domain. C2S requests use <c>0xB000-0xB03F</c>,
/// S2C replies and pushes use <c>0xB040-0xB07F</c>. Never reassign or reuse a value.</para>
/// </summary>
public static class PlaceableOpCode
{
    // -- C2S (0xB000-0xB03F) --------------------------------------------------

    /// <summary>Asks for every placed object of the instance the player is in. Reply: <see cref="S2C.PlacedSnapshot"/>.</summary>
    public const ushort GetPlaced = 0xB000;

    /// <summary>Places the item in an inventory slot. Reply: <see cref="S2C.PlaceResult"/>; the change reaches the instance as <see cref="S2C.PlacedChanged"/>.</summary>
    public const ushort PlaceObject = 0xB001;

    /// <summary>Moves or rotates a placed object. Reply: <see cref="S2C.PlaceResult"/>.</summary>
    public const ushort MoveObject = 0xB002;

    /// <summary>Picks a placed object up into the inventory. Reply: <see cref="S2C.PlaceResult"/>.</summary>
    public const ushort PickUpObject = 0xB003;

    // -- S2C (0xB040-0xB07F) --------------------------------------------------

    /// <summary>Reply to <see cref="GetPlaced"/>.</summary>
    public const ushort PlacedSnapshot = 0xB040;

    /// <summary>Reply to <see cref="PlaceObject"/>, <see cref="MoveObject"/> and <see cref="PickUpObject"/>.</summary>
    public const ushort PlaceResult = 0xB041;

    /// <summary>Push to every player of an instance: objects were added, changed or removed.</summary>
    public const ushort PlacedChanged = 0xB042;
}
