// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nosum.Gameplay.Items;
using Nosum.Gameplay.Text;

namespace Nosum.Gameplay.Coops;

/// <summary>Why a co-op action was refused by the rules.</summary>
public enum CoopVerdict
{
    /// <summary>Allowed.</summary>
    Ok = 0,

    /// <summary>The co-op name is not valid.</summary>
    InvalidName = 1,

    /// <summary>The item id is not in the catalogue.</summary>
    UnknownItem = 2,

    /// <summary>The item cannot be put in a warehouse.</summary>
    NotTradable = 3,

    /// <summary>The quantity is zero or negative.</summary>
    InvalidQuantity = 4,

    /// <summary>The member may not take that much.</summary>
    NotAllowed = 5,
}

/// <summary>The pure rules of co-ops (docs/adr/ADR-014): names and who may withdraw from the shared warehouse.</summary>
public static class CoopRules
{
    /// <summary>Shortest co-op name (plain text or each translation).</summary>
    public const int NameMinLength = 3;

    /// <summary>Longest co-op name (plain text or each translation).</summary>
    public const int NameMaxLength = 40;

    /// <summary>How long an invitation stays open, hours.</summary>
    public const int InviteHours = 72;

    /// <summary>How many warehouse log lines a snapshot carries.</summary>
    public const int RecentLogLines = 20;

    /// <summary>Checks a co-op name typed by a player: plain text or an <c>i18n{</c> envelope (docs/adr/ADR-010 D5).</summary>
    /// <param name="name">The name as typed.</param>
    /// <returns><see cref="CoopVerdict.Ok"/> or <see cref="CoopVerdict.InvalidName"/>.</returns>
    public static CoopVerdict ValidateName(string? name)
        => LocalizedText.IsValid(name, NameMinLength, NameMaxLength) ? CoopVerdict.Ok : CoopVerdict.InvalidName;

    /// <summary>Checks that an item and quantity may move in or out of a warehouse.</summary>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    /// <returns><see cref="CoopVerdict.Ok"/> or the reason.</returns>
    public static CoopVerdict ValidateStack(int itemId, int quantity)
    {
        if (!ItemCatalog.TryGet(itemId, out ItemDefinition? item))
        {
            return CoopVerdict.UnknownItem;
        }

        if (!item.IsTradable)
        {
            return CoopVerdict.NotTradable;
        }

        return quantity < 1 ? CoopVerdict.InvalidQuantity : CoopVerdict.Ok;
    }

    /// <summary>
    /// How many of an item a member may withdraw: the founder takes anything in stock; a member may take back what they themselves
    /// put in and have not yet taken out (their net contribution), never more than is in stock.
    /// </summary>
    /// <param name="isFounder">Whether the member is the founder.</param>
    /// <param name="inStock">How many the warehouse holds.</param>
    /// <param name="contributed">The member's deposits of the item minus their withdrawals of it.</param>
    /// <returns>The most the member may withdraw now; zero when nothing.</returns>
    public static int Withdrawable(bool isFounder, int inStock, long contributed)
    {
        if (isFounder)
        {
            return inStock;
        }

        return (int)System.Math.Clamp(contributed, 0, inStock);
    }

    /// <summary>Checks a withdrawal.</summary>
    /// <param name="isFounder">Whether the member is the founder.</param>
    /// <param name="inStock">How many the warehouse holds.</param>
    /// <param name="contributed">The member's net contribution of the item.</param>
    /// <param name="quantity">How many they ask for.</param>
    /// <returns><see cref="CoopVerdict.Ok"/>, or <see cref="CoopVerdict.NotAllowed"/> when it is more than they may take.</returns>
    public static CoopVerdict CanWithdraw(bool isFounder, int inStock, long contributed, int quantity)
        => quantity >= 1 && quantity <= Withdrawable(isFounder, inStock, contributed) ? CoopVerdict.Ok : CoopVerdict.NotAllowed;
}
