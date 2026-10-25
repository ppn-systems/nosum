// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nosum.Gameplay.Maps;

namespace Nosum.Gameplay.Visits;

/// <summary>What a player can do in a private place (farm or farmhouse), for the rule "the owner, or a visitor the owner let in" (docs/adr/ADR-019 D3).</summary>
public enum PlaceAction
{
    /// <summary>Walk and look around, take part in trades and co-op invitations with the others present.</summary>
    Walk = 0,

    /// <summary>Water a crop that is planted.</summary>
    Water = 1,

    /// <summary>Till the soil.</summary>
    Till = 2,

    /// <summary>Plant a seed.</summary>
    Plant = 3,

    /// <summary>Harvest a ripe crop.</summary>
    Harvest = 4,

    /// <summary>Place a piece of furniture.</summary>
    PlaceObject = 5,

    /// <summary>Move or rotate a placed piece.</summary>
    MoveObject = 6,

    /// <summary>Pick a placed piece up into the inventory.</summary>
    PickUpObject = 7,

    /// <summary>Sell items in the farm's shipping bin.</summary>
    UseShippingBin = 8,
}

/// <summary>
/// The default permissions of a visitor. Pure and shared, so every server service asks the same question and the table is tested in one place.
/// Owner question (docs/adr/ADR-019): this default is the safe one. A visitor may walk and water; everything that changes what the owner has
/// (soil, crops, furniture, coins) stays with the owner. A shared (public) map has no owner, so everybody may do what the map allows there.
/// </summary>
public static class VisitRules
{
    /// <summary>Most players one owner may keep on the allow-list.</summary>
    public const int MaxListed = 50;

    /// <summary>Gets whether a co-op's members may visit a player who never changed the setting.</summary>
    public const bool CoopMembersMayVisitByDefault = true;

    /// <summary>Tells whether a player may do an action in a place.</summary>
    /// <param name="instance">The place the player is in.</param>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="action">What they want to do.</param>
    /// <returns><see langword="true"/> for the owner, in a shared place, or for a visitor's walk or water.</returns>
    public static bool MayDo(MapInstance instance, Guid playerId, PlaceAction action)
        => !instance.IsPrivate || instance.OwnerId == playerId || action is PlaceAction.Walk or PlaceAction.Water;

    /// <summary>Tells whether a player is a visitor: standing in a private place that is not their own.</summary>
    /// <param name="instance">The place the player is in.</param>
    /// <param name="playerId">The player's account id.</param>
    /// <returns><see langword="true"/> when the place belongs to somebody else.</returns>
    public static bool IsVisitor(MapInstance instance, Guid playerId) => instance.IsPrivate && instance.OwnerId != playerId;

    /// <summary>
    /// Decides whether a player may enter a private place of another player.
    /// </summary>
    /// <param name="visitorId">The entering player.</param>
    /// <param name="ownerId">The owner of the place.</param>
    /// <param name="ownerIsGuest">Whether the owner is a guest account.</param>
    /// <param name="listed">Whether the owner put the visitor on the allow-list.</param>
    /// <param name="sameCoop">Whether both belong to the same co-op.</param>
    /// <param name="coopMembersMayVisit">The owner's "co-op members may visit" setting.</param>
    /// <returns><see langword="true"/> when entry is allowed.</returns>
    public static bool MayEnter(Guid visitorId, Guid ownerId, bool ownerIsGuest, bool listed, bool sameCoop, bool coopMembersMayVisit)
    {
        if (visitorId == ownerId)
        {
            return true;
        }

        // A guest keeps no allow-list and no co-op visits; their place is private (docs/adr/ADR-019 D2).
        return !ownerIsGuest && (listed || (sameCoop && coopMembersMayVisit));
    }
}
