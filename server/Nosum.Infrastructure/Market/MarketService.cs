// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Market;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Market;
using Nosum.Domain.Market;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Market;

/// <summary>
/// <see cref="IMarketService"/>: the shipping bin and the consignment market on top of <see cref="ITransferService"/>.
/// A listing is escrow: its items left the seller's inventory in the transaction that created the row, and they leave the row only
/// in the transaction that hands them to the buyer (sold) or back to the seller (cancelled, expired). Each listing has a lock, so two
/// buyers - or a buyer and a cancel - are strictly ordered; the status concurrency token backs that up (docs/adr/ADR-013).
/// </summary>
public sealed class MarketService : IMarketService
{
    private const int MaxMine = 50;
    private const int ExpiryBatch = 100;

    private readonly ITransferService _transfers;
    private readonly IInventoryService _inventory;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly TimeProvider _time;
    private readonly StripedAsyncLock _listingLocks = new(64);

    /// <summary>Initializes the service.</summary>
    /// <param name="transfers">Moves items and coins atomically.</param>
    /// <param name="inventory">Reads the seller's slot.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="presence">Where players stand (the shipping bin needs the position).</param>
    /// <param name="maps">The hosted maps (the farm is the map that can be tilled).</param>
    /// <param name="time">The clock for expiry; tests pass a fake one.</param>
    public MarketService(ITransferService transfers, IInventoryService inventory, IUnitOfWorkFactory unitOfWork, IMapPresence presence, IMapCatalog maps, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(time);

        _transfers = transfers;
        _inventory = inventory;
        _unitOfWork = unitOfWork;
        _presence = presence;
        _maps = maps;
        _time = time;
    }

    /// <inheritdoc/>
    public async Task<ShipOutcome> ShipAsync(IConnection connection, Guid playerId, int slot, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // The bin is on the farm map; the position is the server's own record.
        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map) || map.Tillable is null)
        {
            return new ShipOutcome(MarketStatus.NotAtBin, 0);
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        if (!ShippingBinRules.InReach(feetX, feetY, map.Grid.TileSize))
        {
            return new ShipOutcome(MarketStatus.NotAtBin, 0);
        }

        if (!ShippingPrices.TryGet(itemId, out int each))
        {
            return new ShipOutcome(MarketStatus.NotShippable, 0);
        }

        InventorySlotView held = await _inventory.PeekSlotAsync(playerId, slot, cancellationToken).ConfigureAwait(false);
        if (held.ItemId != itemId)
        {
            return new ShipOutcome(MarketStatus.InvalidSlot, 0);
        }

        int count = quantity <= 0 ? held.Quantity : quantity;
        if (count > held.Quantity)
        {
            return new ShipOutcome(MarketStatus.NotEnough, 0);
        }

        long earned = (long)each * count;
        TransferStatus status = await _transfers.ExecuteAsync(
            [new TransferLeg(TransferParty.Player(playerId), itemId, -count, earned)],
            "shipping.sell",
            null,
            cancellationToken).ConfigureAwait(false);
        return status == TransferStatus.Ok ? new ShipOutcome(MarketStatus.Ok, earned) : new ShipOutcome(ToStatus(status), 0);
    }

    /// <inheritdoc/>
    public async Task<MarketStatus> CreateListingAsync(Guid playerId, int slot, int itemId, int quantity, long price, CancellationToken cancellationToken = default)
    {
        MarketVerdict verdict = MarketRules.ValidateListing(itemId, quantity, price, 0);
        if (verdict != MarketVerdict.Ok)
        {
            return ToStatus(verdict);
        }

        InventorySlotView held = await _inventory.PeekSlotAsync(playerId, slot, cancellationToken).ConfigureAwait(false);
        if (held.ItemId != itemId)
        {
            return MarketStatus.InvalidSlot;
        }

        if (held.Quantity < quantity)
        {
            return MarketStatus.NotEnough;
        }

        MarketStatus refusal = MarketStatus.Ok;
        TransferStatus status = await _transfers.ExecuteAsync(
            [new TransferLeg(TransferParty.Player(playerId), itemId, -quantity)],
            "market.list",
            async (unit, token) =>
            {
                DateTimeOffset now = _time.GetUtcNow();
                IRepository<MarketListing> listings = unit.Repository<MarketListing>();
                int active = await listings.CountAsync(l => l.SellerId == playerId && l.Status == ListingStatus.Active, token).ConfigureAwait(false);
                if (MarketRules.ValidateListing(itemId, quantity, price, active) is { } found and not MarketVerdict.Ok)
                {
                    refusal = ToStatus(found);
                    return TransferStatus.Refused;
                }

                listings.Add(new MarketListing
                {
                    SellerId = playerId,
                    ItemId = itemId,
                    Quantity = quantity,
                    Price = price,
                    Status = ListingStatus.Active,
                    CreatedAt = now,
                    ExpiresAt = now.AddHours(CoinRules.ListingHours),
                });
                return TransferStatus.Ok;
            },
            cancellationToken).ConfigureAwait(false);
        return status == TransferStatus.Refused ? refusal : ToStatus(status);
    }

    /// <inheritdoc/>
    public async Task<MarketStatus> BuyAsync(Guid buyerId, Guid listingId, CancellationToken cancellationToken = default)
    {
        using (await _listingLocks.EnterAsync(listingId, cancellationToken).ConfigureAwait(false))
        {
            MarketListing? listing = await this.ReadAsync(listingId, cancellationToken).ConfigureAwait(false);
            if (listing is null || listing.Status != ListingStatus.Active)
            {
                return MarketStatus.ListingGone;
            }

            if (listing.ExpiresAt <= _time.GetUtcNow())
            {
                return MarketStatus.Expired;
            }

            if (listing.SellerId == buyerId)
            {
                return MarketStatus.OwnListing;
            }

            long price = listing.Price;
            long proceeds = CoinRules.SellerProceeds(price);
            MarketStatus refusal = MarketStatus.Ok;
            TransferStatus status = await _transfers.ExecuteAsync(
                [
                    new TransferLeg(TransferParty.Player(buyerId), listing.ItemId, listing.Quantity, -price),
                    new TransferLeg(TransferParty.Player(listing.SellerId), 0, 0, proceeds),
                ],
                "market.buy",
                async (unit, token) =>
                {
                    MarketListing? tracked = await unit.Repository<MarketListing>().FindAsync(listingId, token).ConfigureAwait(false);
                    if (tracked is null || tracked.Status != ListingStatus.Active)
                    {
                        refusal = MarketStatus.ListingGone;
                        return TransferStatus.Refused;
                    }

                    tracked.Status = ListingStatus.Sold;
                    tracked.BuyerId = buyerId;
                    tracked.ClosedAt = _time.GetUtcNow();
                    return TransferStatus.Ok;
                },
                cancellationToken).ConfigureAwait(false);
            return status == TransferStatus.Refused ? refusal : ToStatus(status);
        }
    }

    /// <inheritdoc/>
    public async Task<MarketStatus> CancelAsync(Guid playerId, Guid listingId, CancellationToken cancellationToken = default)
    {
        using (await _listingLocks.EnterAsync(listingId, cancellationToken).ConfigureAwait(false))
        {
            MarketListing? listing = await this.ReadAsync(listingId, cancellationToken).ConfigureAwait(false);
            if (listing is null || listing.Status != ListingStatus.Active)
            {
                return MarketStatus.ListingGone;
            }

            if (listing.SellerId != playerId)
            {
                return MarketStatus.NotOwner;
            }

            return ToStatus(await this.ReturnAsync(listing, ListingStatus.Cancelled, "market.cancel", cancellationToken).ConfigureAwait(false));
        }
    }

    /// <inheritdoc/>
    public async Task<ListingsPageView> BrowseAsync(int page, IReadOnlyCollection<int> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        int[] ids = [.. itemIds.Take(32)];
        bool any = ids.Length == 0;
        int index = Math.Max(0, page);
        DateTimeOffset now = _time.GetUtcNow();
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<MarketListing> listings = unit.Repository<MarketListing>();
            int total = await listings.CountAsync(l => l.Status == ListingStatus.Active && l.ExpiresAt > now && (any || ids.Contains(l.ItemId)), cancellationToken).ConfigureAwait(false);
            List<MarketListing> rows = await listings.PageAsync(
                l => l.Status == ListingStatus.Active && l.ExpiresAt > now && (any || ids.Contains(l.ItemId)),
                l => l.CreatedAt,
                index * CoinRules.ListingsPerPage,
                CoinRules.ListingsPerPage,
                cancellationToken).ConfigureAwait(false);
            return new ListingsPageView(now, index, total, [.. rows.Select(ViewOf)]);
        }
    }

    /// <inheritdoc/>
    public async Task<ListingsPageView> MineAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<MarketListing> rows = await unit.Repository<MarketListing>().PageAsync(
                l => l.SellerId == playerId && l.Status == ListingStatus.Active,
                l => l.CreatedAt,
                0,
                MaxMine,
                cancellationToken).ConfigureAwait(false);
            return new ListingsPageView(now, 0, rows.Count, [.. rows.Select(ViewOf)]);
        }
    }

    /// <inheritdoc/>
    public async Task<int> ExpireDueAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        List<Guid> due;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<MarketListing> rows = await unit.Repository<MarketListing>().PageAsync(
                l => l.Status == ListingStatus.Active && l.ExpiresAt <= now,
                l => l.ExpiresAt,
                0,
                ExpiryBatch,
                cancellationToken).ConfigureAwait(false);
            due = [.. rows.Select(l => l.Id)];
        }

        int returned = 0;
        foreach (Guid id in due)
        {
            using (await _listingLocks.EnterAsync(id, cancellationToken).ConfigureAwait(false))
            {
                MarketListing? listing = await this.ReadAsync(id, cancellationToken).ConfigureAwait(false);
                if (listing is { Status: ListingStatus.Active } && await this.ReturnAsync(listing, ListingStatus.Expired, "market.expire", cancellationToken).ConfigureAwait(false) == TransferStatus.Ok)
                {
                    returned++;
                }
            }
        }

        return returned;
    }

    private static ListingView ViewOf(MarketListing l) => new(l.Id, l.SellerId, l.ItemId, l.Quantity, l.Price, l.ExpiresAt);

    private async Task<MarketListing?> ReadAsync(Guid listingId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            return await unit.Repository<MarketListing>().FindAsync(listingId, cancellationToken).ConfigureAwait(false);
        }
    }

    // Items back to the seller and the listing closed, in one transaction. A seller with a full inventory is refused (NoSpace) and the listing stays.
    private async Task<TransferStatus> ReturnAsync(MarketListing listing, ListingStatus closeAs, string reason, CancellationToken cancellationToken)
    {
        Guid id = listing.Id;
        return await _transfers.ExecuteAsync(
            [new TransferLeg(TransferParty.Player(listing.SellerId), listing.ItemId, listing.Quantity)],
            reason,
            async (unit, token) =>
            {
                MarketListing? tracked = await unit.Repository<MarketListing>().FindAsync(id, token).ConfigureAwait(false);
                if (tracked is null || tracked.Status != ListingStatus.Active)
                {
                    return TransferStatus.Refused;
                }

                tracked.Status = closeAs;
                tracked.ClosedAt = _time.GetUtcNow();
                return TransferStatus.Ok;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static MarketStatus ToStatus(TransferStatus status) => status switch
    {
        TransferStatus.Ok => MarketStatus.Ok,
        TransferStatus.NotEnoughItems => MarketStatus.NotEnough,
        TransferStatus.NotEnoughCoins => MarketStatus.NotEnoughCoins,
        TransferStatus.NoSpace => MarketStatus.NoSpace,
        TransferStatus.TooBig => MarketStatus.TooBig,
        TransferStatus.Refused => MarketStatus.ListingGone,
        _ => MarketStatus.Conflict,
    };

    private static MarketStatus ToStatus(MarketVerdict verdict) => verdict switch
    {
        MarketVerdict.Ok => MarketStatus.Ok,
        MarketVerdict.UnknownItem => MarketStatus.UnknownItem,
        MarketVerdict.NotTradable => MarketStatus.NotTradable,
        MarketVerdict.InvalidQuantity => MarketStatus.InvalidQuantity,
        MarketVerdict.InvalidPrice => MarketStatus.InvalidPrice,
        _ => MarketStatus.TooManyListings,
    };
}
