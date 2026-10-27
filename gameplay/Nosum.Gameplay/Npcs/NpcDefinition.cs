// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Nosum.Gameplay.Npcs;

/// <summary>
/// A merchant who stands behind a counter (docs/adr/ADR-043). The counter itself is a <c>Maps.ShopPoint</c> and every rule stays on the server; an NPC is only how the client
/// presents that counter, so nothing about it is sent on the wire. Names, roles and lines are not stored here: they are localized by key, <c>Npc_{Id}_Name</c> and so on.
/// </summary>
/// <param name="Id">Stable key, for example <c>seed_keeper</c>; it names the localized text.</param>
/// <param name="MapId">The map the merchant stands on (<c>GameMaps</c>).</param>
/// <param name="CounterId">The counter the merchant serves: the <c>ShopId</c> of the map's <c>ShopPoint</c>.</param>
/// <param name="SpriteKey">The merchant's world sprite: <c>assets/sprites/npcs/{SpriteKey}.png</c>, two 16 x 32 idle frames side by side.</param>
/// <param name="PortraitKey">The merchant's portrait: <c>assets/sprites/npcs/{PortraitKey}.png</c>, 32 x 32.</param>
/// <param name="TileX">Tile x of the merchant's feet.</param>
/// <param name="TileY">Tile y of the merchant's feet.</param>
/// <param name="Actions">What the dialog box offers besides Leave.</param>
public sealed record NpcDefinition(string Id, string MapId, int CounterId, string SpriteKey, string PortraitKey, int TileX, int TileY, NpcActions Actions)
{
    /// <summary>Builds the localization key of one part of the merchant's text.</summary>
    /// <param name="part">The part: <c>Name</c>, <c>Role</c>, <c>Greeting</c>, <c>Talk1</c> or <c>Talk2</c>.</param>
    /// <returns>The key, for example <c>Npc_seed_keeper_Name</c>.</returns>
    public string TextKey(string part) => "Npc_" + this.Id + "_" + part;
}
