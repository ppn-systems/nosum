// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Quests;
using Nosum.Abstractions.Shops;
using Nosum.Contracts.Shop;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Quests;

namespace Nosum.Infrastructure.Shops;

/// <summary>
/// <see cref="IShopService"/>: validates against <see cref="ShopRules"/> and the player's presence, then buys through <see cref="ITransferService"/>
/// (coins out, items in, one transaction, audit rows with reason <c>shop.buy</c>).
/// </summary>
public sealed class ShopService : IShopService
{
    private readonly ITransferService _transfers;
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly GameCalendar _calendar;
    private readonly TimeProvider _time;
    private readonly IQuestService? _quests;

    /// <summary>Initializes the service.</summary>
    /// <param name="transfers">Moves coins and items atomically.</param>
    /// <param name="presence">Where players stand.</param>
    /// <param name="maps">The hosted maps (tile size).</param>
    /// <param name="calendar">The server calendar: seeds are only sold in a season their crop grows in (docs/adr/ADR-018).</param>
    /// <param name="time">The server clock; tests pass a fake one.</param>
    /// <param name="quests">Told what the player bought (docs/adr/ADR-036 D3); <see langword="null"/> to tell nobody.</param>
    public ShopService(ITransferService transfers, IMapPresence presence, IMapCatalog maps, GameCalendar calendar, TimeProvider time, IQuestService? quests = null)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);

        _transfers = transfers;
        _presence = presence;
        _maps = maps;
        _calendar = calendar;
        _time = time;
        _quests = quests;
    }

    /// <inheritdoc/>
    public async Task<ShopOutcome> BuyAsync(IConnection connection, Guid playerId, int shopId, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        ShopVerdict verdict = ShopRules.Price(shopId, itemId, quantity, out long total);
        if (verdict != ShopVerdict.Ok)
        {
            return new ShopOutcome(verdict switch
            {
                ShopVerdict.UnknownShop => ShopStatus.UnknownShop,
                ShopVerdict.NotSold => ShopStatus.NotSold,
                _ => ShopStatus.InvalidQuantity,
            }, 0);
        }

        if (!ShopRules.IsInSeason(shopId, itemId, _calendar, _time.GetUtcNow().ToUnixTimeMilliseconds()))
        {
            return new ShopOutcome(ShopStatus.OutOfSeason, 0);
        }

        if (!this.IsAtShop(connection, shopId))
        {
            return new ShopOutcome(ShopStatus.NotAtShop, 0);
        }

        TransferStatus status = await _transfers.ExecuteAsync(
            [new TransferLeg(TransferParty.Player(playerId), itemId, quantity, -total)],
            "shop.buy",
            null,
            cancellationToken).ConfigureAwait(false);
        if (status == TransferStatus.Ok && ItemCatalog.TryGet(itemId, out ItemDefinition? bought) && bought.Kind is ItemKind.Seed or ItemKind.TreeSeed)
        {
            await _quests.TryReportAsync(playerId, new QuestEvent(QuestEventKind.SeedBought, itemId, quantity), cancellationToken).ConfigureAwait(false);
        }

        return status switch
        {
            TransferStatus.Ok => new ShopOutcome(ShopStatus.Ok, total),
            TransferStatus.NotEnoughCoins => new ShopOutcome(ShopStatus.NotEnoughCoins, 0),
            TransferStatus.NoSpace => new ShopOutcome(ShopStatus.NoSpace, 0),
            _ => new ShopOutcome(ShopStatus.Conflict, 0),
        };
    }

    // The position is the server's own record, never a value from the packet.
    private bool IsAtShop(IConnection connection, int shopId) => ShopCounterReach.IsAt(_presence, _maps, connection, shopId);
}
