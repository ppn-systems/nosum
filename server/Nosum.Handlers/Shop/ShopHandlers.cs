// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nosum.Abstractions.Shops;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;
using Nosum.Handlers.Session;

namespace Nosum.Handlers.Shop;

/// <summary>Buying from a shop. The server prices, checks where the player stands and moves coins and items (docs/adr/ADR-013 amendments); a handler only relays the intent.</summary>
[PacketHandler("Nosum.Shop")]
public static partial class ShopHandlers
{
    [Inject] private static IShopService s_shops = null!;

    /// <summary>Buys an item from a shop.</summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the reply was sent.</returns>
    [PacketOpcode(ShopOpCode.BuyFromShop)]
    [PacketPermission(PermissionLevel.USER)]
    [PacketEncryption(true)]
    [PacketRateLimit(8, 4, PolicyId = "shop-buy")]
    public static async ValueTask HandleBuyFromShopAsync(IPacketContext<BuyFromShop> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        ushort sequence = context.Packet.SequenceId;
        int shopId = context.Packet.ShopId;
        int itemId = context.Packet.ItemId;
        int quantity = context.Packet.Quantity;
        PlayerIdentity player = context.Connection.GetPlayer();

        ShopOutcome outcome = await s_shops.BuyAsync(context.Connection, player.PlayerId, shopId, itemId, quantity, context.CancellationToken).ConfigureAwait(false);
        await Replies.ReplyAsync<BuyFromShop, ShopResult>(context, sequence, r =>
        {
            r.Status = outcome.Status;
            r.Spent = outcome.Spent;
        }).ConfigureAwait(false);
    }
}
