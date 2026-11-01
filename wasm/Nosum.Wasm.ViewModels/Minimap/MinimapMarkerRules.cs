// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Npcs;

namespace Nosum.Wasm.ViewModels.Minimap;

/// <summary>
/// The minimap's dot colours for the places a player looks for (docs/adr/ADR-072): a shop counter is a white dot, a neighbour who only talks is orange,
/// so a merchant is found at a glance on the card instead of among the other NPCs. Pure: the map scene asks, nothing is drawn here.
/// </summary>
public static class MinimapMarkerRules
{
    /// <summary>A merchant (an NPC that buys or sells): white, the one colour no other dot uses.</summary>
    public const int ShopColour = unchecked((int)0xF5F5F5FFu);

    /// <summary>An NPC who only talks: orange.</summary>
    public const int TalkColour = unchecked((int)0xFF9F43FFu);

    /// <summary>Returns the dot colour of an NPC on the minimap (0xRRGGBBAA).</summary>
    /// <param name="actions">What the NPC offers in its dialogue box.</param>
    /// <returns><see cref="ShopColour"/> when the NPC buys or sells, otherwise <see cref="TalkColour"/>.</returns>
    public static int NpcColour(NpcActions actions) => (actions & NpcActions.Buy) != 0 ? ShopColour : TalkColour;
}
