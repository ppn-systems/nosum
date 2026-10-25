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

    /// <summary>Whether friends may visit until the owner turns it off (docs/adr/ADR-029 D4).</summary>
    public const bool FriendsMayVisitByDefault = true;

    /// <summary>Tells whether a player may do an action in a place.</summary>
    /// <param name="instance">The place the player is in.</param>
    /// <param name="playerId">The player's account id.</param>
    /// <param name="action">What they want to do.</param>
    /// <returns><see langword="true"/> for the owner, in a shared place, on co-op land (membership is checked where it is entered and worked), or for a visitor's walk or water.</returns>
    public static bool MayDo(MapInstance instance, Guid playerId, PlaceAction action)
        => !instance.IsPrivate || instance.OwnerId == playerId || instance.IsCoopLand || action is PlaceAction.Walk or PlaceAction.Water;

    /// <summary>Tells whether a player is a visitor: standing in a private place that is not their own.</summary>
    /// <param name="instance">The place the player is in.</param>
    /// <param name="playerId">The player's account id.</param>
    /// <returns><see langword="true"/> when the place belongs to somebody else; co-op land is never a visit.</returns>
    public static bool IsVisitor(MapInstance instance, Guid playerId) => instance.IsPrivate && !instance.IsCoopLand && instance.OwnerId != playerId;

    /// <summary>
    /// Decides whether a player may enter a private place of another player.
    /// </summary>
    /// <param name="visitorId">The entering player.</param>
    /// <param name="ownerId">The owner of the place.</param>
    /// <param name="ownerIsGuest">Whether the owner is a guest account.</param>
    /// <param name="listed">Whether the owner put the visitor on the allow-list.</param>
    /// <param name="sameCoop">Whether both belong to the same co-op.</param>
    /// <param name="coopMembersMayVisit">The owner's "co-op members may visit" setting.</param>
    /// <param name="isFriend">Whether the two are friends (docs/adr/ADR-029 D4).</param>
    /// <param name="friendsMayVisit">The owner's "friends may visit" setting.</param>
    /// <param name="blocked">Whether the owner blocked the visitor (docs/adr/ADR-029 D3): a block beats every other source.</param>
    /// <returns><see langword="true"/> when entry is allowed.</returns>
    public static bool MayEnter(Guid visitorId, Guid ownerId, bool ownerIsGuest, bool listed, bool sameCoop, bool coopMembersMayVisit, bool isFriend = false, bool friendsMayVisit = FriendsMayVisitByDefault, bool blocked = false)
    {
        if (visitorId == ownerId)
        {
            return true;
        }

        // A guest keeps no allow-list, no co-op visits and no friends; their place is private (docs/adr/ADR-019 D2).
        return !ownerIsGuest && !blocked && (listed || (sameCoop && coopMembersMayVisit) || (isFriend && friendsMayVisit));
    }
}
