// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Shop;
using Nosum.Contracts.Shop.C2S;
using Nosum.Contracts.Shop.S2C;

namespace Nosum.Wasm.Socket.Shop;

/// <summary>The server's answer to one purchase, with the purchase it answers: the item and quantity asked for, and the coins the server took.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="ItemId">The item asked for.</param>
/// <param name="Quantity">How many were asked for.</param>
/// <param name="Spent">The coins the server took; zero unless the purchase went through.</param>
public sealed record ShopPurchaseInfo(ShopStatus Status, int ItemId, int Quantity, long Spent);

/// <summary>
/// Sends purchases to the server (docs/adr/ADR-013 amendments). The catalogue and prices are shared rules (<c>ShopCatalog</c>), so there is nothing to
/// load; the answer arrives as <see cref="Completed"/>, and the items and coins as the usual inventory and wallet pushes.
/// </summary>
public sealed class ShopChannel
{
    private readonly IGameSocket _socket;

    /// <summary>Initializes the channel.</summary>
    /// <param name="socket">The game connection.</param>
    public ShopChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);
        _socket = socket;
    }

    /// <summary>Raised with the server's answer to a purchase: the status, the purchase it answers and the coins spent. May run on any thread.</summary>
    public event Action<ShopPurchaseInfo>? Completed;

    /// <summary>Buys an item.</summary>
    /// <param name="shopId">The shop.</param>
    /// <param name="itemId">The item.</param>
    /// <param name="quantity">How many.</param>
    public void Buy(int shopId, int itemId, int quantity) => _ = this.BuyAsync(shopId, itemId, quantity);

    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task BuyAsync(int shopId, int itemId, int quantity)
    {
        try
        {
            using PacketScope<BuyFromShop> request = PacketFactory<BuyFromShop>.Acquire();
            request.Value.ShopId = shopId;
            request.Value.ItemId = itemId;
            request.Value.Quantity = quantity;
            ShopResult reply = await _socket.RequestAsync<ShopResult>(request.Value).ConfigureAwait(false);
            this.Completed?.Invoke(new ShopPurchaseInfo(reply.Status, itemId, quantity, reply.Spent));
        }
        catch (Exception)
        {
            // Connection dropped.
        }
    }
}
