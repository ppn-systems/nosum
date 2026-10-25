// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Extensions;
using Nalix.Codec.Pooling;
using Nalix.Runtime.Extensions;
using Nosum.Abstractions.Crafting;
using Nosum.Contracts.Placeable;
using Nosum.Contracts.Placeable.C2S;
using Nosum.Contracts.Placeable.S2C;

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
        CraftOutcome outcome = await s_crafting.CraftAsync(context.Connection, context.Packet.RecipeId, context.Packet.Times, context.CancellationToken).ConfigureAwait(false);

        PacketScope<CraftResult> lease = PacketFactory<CraftResult>.Acquire();
        try
        {
            lease.Value.SequenceId = sequence;
            lease.Value.Status = outcome.Status;
            lease.Value.RecipeId = context.Packet.RecipeId;
            lease.Value.Times = (byte)outcome.Times;
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
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

        PacketScope<RecipeBook> lease = PacketFactory<RecipeBook>.Acquire();
        try
        {
            lease.Value.SequenceId = sequence;
            lease.Value.Unlocked.Clear();
            lease.Value.Unlocked.AddRange(unlocked);
            await context.Sender.ReplyAsync(lease.Value).DisposeOnCompletionAsync(lease).ConfigureAwait(false);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
