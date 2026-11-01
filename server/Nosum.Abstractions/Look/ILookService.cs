// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Contracts.Look;
using Nosum.Contracts.Look.S2C;
using Nosum.Abstractions.Pets;
using Nosum.Gameplay.Looks;

namespace Nosum.Abstractions.Look;

/// <summary>One player on a map with their look.</summary>
/// <param name="PlayerId">The id the player has on the map (not the account id).</param>
/// <param name="Look">The look.</param>
/// <param name="Nickname">The player's nickname, sent with the look so the client caches it by player id.</param>
/// <param name="Pet">The player's pet (kind and name), or none (docs/adr/ADR-075 D3).</param>
public readonly record struct PlayerLookView(uint PlayerId, PlayerLook Look, string Nickname, PetLook Pet = default)
{
    /// <summary>Copies the look into a wire row (ids only, ADR-006 D6).</summary>
    /// <param name="row">The row to fill.</param>
    public void FillRow(LookRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.PlayerId = this.PlayerId;
        row.SkinId = (byte)this.Look.SkinId;
        row.HairStyleId = (byte)this.Look.HairStyleId;
        row.HairDyeId = (byte)this.Look.HairDyeId;
        row.ShirtItemId = this.Look.ShirtItemId;
        row.PantsItemId = this.Look.PantsItemId;
        row.HatItemId = this.Look.HatItemId;
        row.FeetItemId = this.Look.FeetItemId;
        row.Nickname = this.Nickname;
        row.PetKind = this.Pet.Kind;
        row.PetName = this.Pet.Name ?? string.Empty;
    }
}

/// <summary>
/// Player looks and the wardrobe (docs/adr/ADR-014). The server owns every look: a new player gets the default one (plain garments; clothes are bought at the shop); the player changes avatar parts and wears clothing items they own; the server validates, saves,
/// and sends only ids to the players who can see the player (docs/adr/ADR-022).
/// </summary>
public interface ILookService
{
    /// <summary>Reads what a client needs to draw one player: look ids and nickname. Used when the player enters someone's interest set (docs/adr/ADR-022 D3).</summary>
    /// <param name="playerId">The player's id on the map.</param>
    /// <param name="accountId">The player's account id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The look and nickname.</returns>
    Task<PlayerLookView> ViewAsync(uint playerId, Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the looks of the players the connection's player can see (the players in their chunk interest set, docs/adr/ADR-022) and announces the player's own look to the
    /// players who can see them (a client calls this after joining a map or changing map).
    /// </summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="playerId">The player's account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The looks of the players in sight, the caller included; empty when the caller is on no map.</returns>
    Task<IReadOnlyList<PlayerLookView>> AnnounceAndGetAsync(IConnection connection, Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Announces the connection's current look and nickname to the players who can see them, themselves included (after a nickname change).</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="playerId">The player's account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the announcement was sent.</returns>
    Task AnnounceAsync(IConnection connection, Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Reads a player's own look (created with the default on first read).</summary>
    /// <param name="playerId">The player.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The look.</returns>
    Task<PlayerLook> GetAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Chooses avatar parts.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="skinId">Skin tone id.</param>
    /// <param name="hairStyleId">Hair style id.</param>
    /// <param name="hairDyeId">Hair dye id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<LookStatus> SetAppearanceAsync(IConnection connection, Guid playerId, int skinId, int hairStyleId, int hairDyeId, CancellationToken cancellationToken = default);

    /// <summary>Wears a clothing item from an inventory slot; the item worn before goes back into the inventory.</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="slot">Inventory slot.</param>
    /// <param name="itemId">The item the player expects in the slot.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<LookStatus> EquipAsync(IConnection connection, Guid playerId, int slot, int itemId, CancellationToken cancellationToken = default);

    /// <summary>Takes a worn item off into the inventory (a plain shirt or trousers, or no hat, shows instead).</summary>
    /// <param name="connection">The player's connection.</param>
    /// <param name="playerId">The player.</param>
    /// <param name="wearSlot">Which slot, as the gameplay wear slot value.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The status.</returns>
    Task<LookStatus> UnequipAsync(IConnection connection, Guid playerId, int wearSlot, CancellationToken cancellationToken = default);
}
