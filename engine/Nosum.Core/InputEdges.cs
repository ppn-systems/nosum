// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Core;

/// <summary>
/// Keys pressed since the previous frame (not held): one-shot commands. The platform batches them
/// into the same integer as the held directions, above the low byte, so a frame still costs one
/// interop crossing (docs/adr/ADR-002).
/// </summary>
[Flags]
public enum InputEdges
{
    /// <summary>Nothing pressed.</summary>
    None = 0,

    /// <summary>The action key (E or Space): use what is held on the tile in front.</summary>
    Action = 1,

    /// <summary>Number key 1.</summary>
    Number1 = 1 << 1,

    /// <summary>Number key 2.</summary>
    Number2 = 1 << 2,

    /// <summary>Number key 3.</summary>
    Number3 = 1 << 3,

    /// <summary>Number key 4.</summary>
    Number4 = 1 << 4,

    /// <summary>Number key 5.</summary>
    Number5 = 1 << 5,

    /// <summary>Number key 6.</summary>
    Number6 = 1 << 6,

    /// <summary>Number key 7.</summary>
    Number7 = 1 << 7,

    /// <summary>Number key 8.</summary>
    Number8 = 1 << 8,

    /// <summary>Number key 9.</summary>
    Number9 = 1 << 9,

    /// <summary>The inventory key (I): show or hide the inventory.</summary>
    Inventory = 1 << 10,

    /// <summary>The rotate key (R): turn the furniture held or carried.</summary>
    Rotate = 1 << 11,

    /// <summary>The grab key (F): pick the furniture in front up to move it, or put a carried piece back where it was.</summary>
    Grab = 1 << 12,

    /// <summary>The pick-up key (Q): take the furniture in front back into the inventory.</summary>
    PickUp = 1 << 13,

    /// <summary>The market key (M): no longer bound; the market opens only at the trader's counter in Town (docs/adr/ADR-034). The value stays so the bit layout does not change.</summary>
    Market = 1 << 14,

    /// <summary>The wardrobe key (C): show or hide the wardrobe panel.</summary>
    Wardrobe = 1 << 15,

    /// <summary>The trade key (T): show or hide the trade window.</summary>
    Trade = 1 << 16,

    /// <summary>The co-op key (G): show or hide the co-op panel.</summary>
    Coop = 1 << 17,

    /// <summary>The close key (Escape): close the open panel.</summary>
    Close = 1 << 18,

    /// <summary>The help key (H or ?): show or hide the keyboard help.</summary>
    Help = 1 << 19,

    /// <summary>The profile key (N): show or hide the profile panel (change nickname).</summary>
    Profile = 1 << 20,
}

/// <summary>Helpers for the packed input integer the platform passes to the frame.</summary>
public static class InputPacking
{
    /// <summary>Bits the held directions occupy; the edges start above them.</summary>
    public const int HeldBits = 8;

    /// <summary>Packs held directions and pressed keys into one integer.</summary>
    /// <param name="held">Held directions.</param>
    /// <param name="edges">Keys pressed since the last frame.</param>
    /// <returns>The packed integer.</returns>
    public static int Pack(InputState held, InputEdges edges) => ((int)held & ((1 << HeldBits) - 1)) | ((int)edges << HeldBits);

    /// <summary>Splits a packed integer.</summary>
    /// <param name="packed">The integer from <see cref="Pack"/>.</param>
    /// <param name="held">The held directions.</param>
    /// <param name="edges">The keys pressed since the last frame.</param>
    public static void Unpack(int packed, out InputState held, out InputEdges edges)
    {
        held = (InputState)(packed & ((1 << HeldBits) - 1));
        edges = (InputEdges)(packed >>> HeldBits);
    }

    /// <summary>Returns the number key (1 to 9) pressed, as a zero-based slot.</summary>
    /// <param name="edges">The pressed keys.</param>
    /// <returns>The zero-based slot of the lowest number key pressed, or -1 when none.</returns>
    public static int NumberSlot(InputEdges edges)
    {
        for (int i = 0; i < 9; i++)
        {
            if ((edges & (InputEdges)(1 << (i + 1))) != 0)
            {
                return i;
            }
        }

        return -1;
    }
}
