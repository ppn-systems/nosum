// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Crafting;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Placeable;

/// <summary>Crafting from the inventory, and reading which recipes the player has unlocked. The client names a recipe and a count; the server checks the unlock and moves the items (docs/adr/ADR-024).</summary>
[PacketHandler("Nosum.Crafting")]
public static partial class CraftingHandlers
{
    [Inject] private static ICraftingService s_crafting = null!;

    /// <summary>Crafts a recipe; replies with the outcome. The inventory change follows as an inventory update.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PlaceableOpCode.Craft)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(6, 4, PolicyId = "craft")]
    public static async ValueTask HandleCraftAsync(IPacketContext<Craft> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        ushort recipeId = context.Packet.RecipeId;
        CraftOutcome outcome = await s_crafting.CraftAsync(context.Connection, recipeId, context.Packet.Times, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<Craft, CraftResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.RecipeId = recipeId;
            r.Times = (byte)outcome.Times;
        }).ConfigureAwait(false);
    }

    /// <summary>Replies with the ids of the recipes the player can craft now.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(PlaceableOpCode.GetRecipeBook)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketRateLimit(4, 5, PolicyId = "craft-read")]
    public static async ValueTask HandleGetRecipeBookAsync(IPacketContext<GetRecipeBook> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        IReadOnlyList<int> unlocked = await s_crafting.UnlockedAsync(context.Connection, context.CancellationToken).ConfigureAwait(false);

        await Replies.ReplyAsync<GetRecipeBook, RecipeBook>(context, sequence, r =>
        {
            r.Unlocked.Clear();
            r.Unlocked.AddRange(unlocked);
        }).ConfigureAwait(false);
    }
}
