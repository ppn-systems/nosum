// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Mail;
using Nosum.Abstractions.Market;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Market;
using Nosum.Contracts.Validation;
using Nosum.Domain.Market;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Economy;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mail;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Shops;
using Nosum.Gameplay.Visits;
using Nosum.Infrastructure.Concurrency;
using Nosum.Infrastructure.Economy;
using Nosum.Infrastructure.Quests;

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
    private readonly IFeeSink _feeSink;
    private readonly INicknameDirectory _nicknames;
    private readonly IMailService? _mail;
    private readonly IQuestService? _quests;
    private readonly TimeProvider _time;
    private readonly GameCalendar _calendar;
    private readonly StripedAsyncLock _listingLocks = new(64);

    /// <summary>Initializes the service.</summary>
    /// <param name="transfers">Moves items and coins atomically.</param>
    /// <param name="inventory">Reads the seller's slot.</param>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="presence">Where players stand (the shipping bin needs the position).</param>
    /// <param name="maps">The hosted maps (the farm is the map that can be tilled).</param>
    /// <param name="feeSink">Receives the market fee (a sink: the coins are destroyed).</param>
    /// <param name="nicknames">Resolves seller nicknames for listings.</param>
    /// <param name="time">The clock for expiry; tests pass a fake one.</param>
    /// <param name="calendar">The server calendar: sales are filed under the game day they happened (the price history); the default calendar when <see langword="null"/>.</param>
    /// <param name="mail">Receives a "your listing sold" notice for the seller (docs/adr/ADR-029 D5); <see langword="null"/> to send none.</param>
    /// <param name="quests">Told what the player shipped (docs/adr/ADR-036 D3); <see langword="null"/> to tell nobody.</param>
    public MarketService(ITransferService transfers, IInventoryService inventory, IUnitOfWorkFactory unitOfWork, IMapPresence presence, IMapCatalog maps, IFeeSink feeSink, INicknameDirectory nicknames, TimeProvider time, GameCalendar? calendar = null, IMailService? mail = null, IQuestService? quests = null)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(feeSink);
        ArgumentNullException.ThrowIfNull(nicknames);
        ArgumentNullException.ThrowIfNull(time);

        _transfers = transfers;
        _inventory = inventory;
        _unitOfWork = unitOfWork;
        _presence = presence;
        _maps = maps;
        _feeSink = feeSink;
        _nicknames = nicknames;
        _time = time;
        _calendar = calendar ?? GameCalendar.Default;
        _mail = mail;
        _quests = quests;
    }

    /// <inheritdoc/>
    public async Task<ShipOutcome> ShipAsync(IConnection connection, Guid playerId, int slot, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // The bin is on the personal farm map; the position is the server's own record.
        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map) || map.Tillable is null || map.IsCoop)
        {
            return new ShipOutcome(MarketStatus.NotAtBin, 0);
        }

        // The bin is the owner's: a visitor on somebody's farm cannot sell through it (docs/adr/ADR-019 D3).
        if (!VisitRules.MayDo(player.Instance, player.AccountId, PlaceAction.UseShippingBin))
        {
            return new ShipOutcome(MarketStatus.NotYourBin, 0);
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
        TransferLeg[] shipLegs = [new TransferLeg(TransferParty.Player(playerId), itemId, -count, earned)];
        TransferStatus status = _transfers is TransferService slotShipping
            ? await slotShipping.ExecuteFromSlotAsync(slot, shipLegs, "shipping.sell", null, cancellationToken).ConfigureAwait(false)
            : await _transfers.ExecuteAsync(shipLegs, "shipping.sell", null, cancellationToken).ConfigureAwait(false);
        if (status != TransferStatus.Ok)
        {
            return new ShipOutcome(ToStatus(status), 0);
        }

        await _quests.TryReportAsync(playerId, new QuestEvent(QuestEventKind.ItemShipped, itemId, count), cancellationToken).ConfigureAwait(false);
        return new ShipOutcome(MarketStatus.Ok, earned);
    }

    /// <inheritdoc/>
    public async Task<MarketStatus> CreateListingAsync(IConnection connection, Guid playerId, int slot, int itemId, int quantity, long price, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.IsAtCounter(connection))
        {
            return MarketStatus.NotAtCounter;
        }

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
        TransferLeg[] listingLegs = [new TransferLeg(TransferParty.Player(playerId), itemId, -quantity)];
        TransferEnlistment enlistListing = async (unit, token) =>
        {
            DateTimeOffset now = _time.GetUtcNow();
            IRepository<MarketListing> listings = unit.Repository<MarketListing>();
            int active = await listings.CountAsync(l => l.SellerId == playerId && l.Status == ListingStatus.Active, token).ConfigureAwait(false);

            // The seller's lock (the transfer holds it) orders two listings of one player, so the sum of units is exact (docs/adr/ADR-030 D4).
            List<MarketListing> sameItem = await listings.ListAsync(l => l.SellerId == playerId && l.Status == ListingStatus.Active && l.ItemId == itemId, token).ConfigureAwait(false);
            int listedUnits = sameItem.Sum(l => l.Quantity);
            if (MarketRules.ValidateListing(itemId, quantity, price, active, listedUnits) is { } found and not MarketVerdict.Ok)
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
        };
        TransferStatus status = _transfers is TransferService slotListing
            ? await slotListing.ExecuteFromSlotAsync(slot, listingLegs, "market.list", enlistListing, cancellationToken).ConfigureAwait(false)
            : await _transfers.ExecuteAsync(listingLegs, "market.list", enlistListing, cancellationToken).ConfigureAwait(false);
        return status == TransferStatus.Refused ? refusal : ToStatus(status);
    }

    /// <inheritdoc/>
    public async Task<MarketStatus> BuyAsync(IConnection connection, Guid buyerId, Guid listingId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.IsAtCounter(connection))
        {
            return MarketStatus.NotAtCounter;
        }

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
            long fee = CoinRules.MarketFee(price);
            long proceeds = price - fee;

            // The fee is whatever the buyer pays and the seller does not get; the sink says whether anybody receives it.
            List<TransferLeg> legs =
            [
                new TransferLeg(TransferParty.Player(buyerId), listing.ItemId, listing.Quantity, -price),
                new TransferLeg(TransferParty.Player(listing.SellerId), 0, 0, proceeds),
                .. _feeSink.Collect(fee, "market.buy"),
            ];

            // The row is read once, above, under the listing lock. The save writes it with its Active status as the concurrency token, so a row another
            // process closed in the meantime fails the save (Conflict) and nothing moves; no second read (docs/adr/ADR-035 D1, ADR-013 D5 races).
            MarketListing row = listing;
            TransferStatus status = await _transfers.ExecuteAsync(
                legs,
                "market.buy",
                (unit, token) =>
                {
                    unit.Repository<MarketListing>().Update(row, nameof(MarketListing.Status), nameof(MarketListing.BuyerId), nameof(MarketListing.ClosedAt));
                    DateTimeOffset soldAt = _time.GetUtcNow();
                    row.Status = ListingStatus.Sold;
                    row.BuyerId = buyerId;
                    row.ClosedAt = soldAt;

                    // The sale joins the price history of its item in the same transaction (docs/adr/ADR-030 D4).
                    unit.Repository<MarketSale>().Add(new MarketSale
                    {
                        ItemId = row.ItemId,
                        Quantity = row.Quantity,
                        Price = row.Price,
                        UnitPrice = PriceHistoryRules.UnitPrice(row.Price, row.Quantity),
                        Day = _calendar.DayNumberAt(soldAt.ToUnixTimeMilliseconds()),
                        SoldAt = soldAt,
                    });
                    return Task.FromResult(TransferStatus.Ok);
                },
                cancellationToken).ConfigureAwait(false);
            if (status == TransferStatus.Ok)
            {
                await this.NotifySoldAsync(listing, proceeds).ConfigureAwait(false);
            }

            return ToStatus(status);
        }
    }

    // The sale is done and paid; a failing notice must not turn it into an error for the buyer (docs/adr/ADR-029 D5).
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "The sale is committed; the seller sees it in their wallet and the market even if the notice is lost.")]
    private async Task NotifySoldAsync(MarketListing listing, long proceeds)
    {
        if (_mail is null)
        {
            return;
        }

        try
        {
            await _mail.SendAsync(
                listing.SellerId,
                MailRules.Codes.MarketSold,
                [listing.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), listing.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), proceeds.ToString(System.Globalization.CultureInfo.InvariantCulture)]).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // See above.
        }
    }

    /// <inheritdoc/>
    public async Task<MarketStatus> CancelAsync(IConnection connection, Guid playerId, Guid listingId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!this.IsAtCounter(connection))
        {
            return MarketStatus.NotAtCounter;
        }

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
    public async Task<ListingsPageView> BrowseAsync(int page, IReadOnlyCollection<int> itemIds, MarketSort sort = MarketSort.Newest, int pageSize = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);

        int[] ids = [.. itemIds.Take(32)];
        bool any = ids.Length == 0;
        int index = Math.Max(0, page);

        // The client asks for the rows its panel has room for (docs/adr/ADR-101 amendment 2026-11-01); zero is the default, a larger size is clamped.
        int size = pageSize <= 0 ? CoinRules.ListingsPerPage : Math.Min(pageSize, WireLimits.MaxListingsPageSize);
        DateTimeOffset now = _time.GetUtcNow();
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<MarketListing> listings = unit.Repository<MarketListing>();
            int total = await listings.CountAsync(l => l.Status == ListingStatus.Active && l.ExpiresAt > now && (any || ids.Contains(l.ItemId)), cancellationToken).ConfigureAwait(false);

            // The repository pages newest-key first. A unit price lowest-first page sorts on the negated unit price, so the same descending page gives the ascending order.
            int skip = index * size;
            List<MarketListing> rows = sort switch
            {
                MarketSort.UnitPriceLowest => await listings.PageAsync(
                    l => l.Status == ListingStatus.Active && l.ExpiresAt > now && (any || ids.Contains(l.ItemId)),
                    l => -((double)l.Price / l.Quantity),
                    skip,
                    size,
                    cancellationToken).ConfigureAwait(false),
                MarketSort.UnitPriceHighest => await listings.PageAsync(
                    l => l.Status == ListingStatus.Active && l.ExpiresAt > now && (any || ids.Contains(l.ItemId)),
                    l => (double)l.Price / l.Quantity,
                    skip,
                    size,
                    cancellationToken).ConfigureAwait(false),
                _ => await listings.PageAsync(
                    l => l.Status == ListingStatus.Active && l.ExpiresAt > now && (any || ids.Contains(l.ItemId)),
                    l => l.CreatedAt,
                    skip,
                    size,
                    cancellationToken).ConfigureAwait(false),
            };
            return new ListingsPageView(now, index, total, await this.ViewsAsync(rows, cancellationToken).ConfigureAwait(false));
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
            return new ListingsPageView(now, 0, rows.Count, await this.ViewsAsync(rows, cancellationToken).ConfigureAwait(false));
        }
    }

    /// <inheritdoc/>
    public async Task<PriceHistoryView> GetPriceHistoryAsync(int itemId, CancellationToken cancellationToken = default)
    {
        int today = _calendar.DayNumberAt(_time.GetUtcNow().ToUnixTimeMilliseconds());
        int first = today - PriceHistoryRules.HistoryDays + 1;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<MarketSale> sales = await unit.Repository<MarketSale>().ListAsync(s => s.ItemId == itemId && s.Day >= first, cancellationToken).ConfigureAwait(false);
            List<PriceDayView> days = [];
            foreach (IGrouping<int, MarketSale> day in sales.GroupBy(s => s.Day).OrderBy(g => g.Key))
            {
                PriceSummary summary = PriceHistoryRules.Summarize([.. day.Select(s => s.UnitPrice)]);
                days.Add(new PriceDayView(day.Key, summary.Min, summary.Median, summary.Max, summary.Sales));
            }

            int averageFirst = today - PriceHistoryRules.AverageDays + 1;
            List<long> recent = [.. sales.Where(s => s.Day >= averageFirst).Select(s => s.UnitPrice)];
            long average = recent.Count == 0 ? 0 : PriceHistoryRules.Average(recent);
            return new PriceHistoryView(itemId, today, days, average);
        }
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "Fault isolation: one corrupt listing must not block subsequent expired listings.")]
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
            try
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Fault isolation: one corrupt listing must not block subsequent expired listings.
            }
        }

        return returned;
    }

    private async Task<IReadOnlyList<ListingView>> ViewsAsync(List<MarketListing> rows, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> names = await _nicknames.GetManyAsync(rows.Select(l => l.SellerId), cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(l => new ListingView(l.Id, l.SellerId, l.ItemId, l.Quantity, l.Price, l.ExpiresAt, names.GetValueOrDefault(l.SellerId, string.Empty)))];
    }

    private async Task<MarketListing?> ReadAsync(Guid listingId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            return await unit.Repository<MarketListing>().FindAsync(listingId, cancellationToken).ConfigureAwait(false);
        }
    }

    // Items back to the seller and the listing closed, in one transaction. A seller with a full inventory is refused (NoSpace) and the listing stays.
    // Listing, buying and cancelling happen at the trader's counter in Town (docs/adr/ADR-034); the position is the server's own record, never a value from the packet.
    private bool IsAtCounter(IConnection connection)
    {
        if (!_presence.TryGet(connection, out PlayerPresence? player)
            || !GameMaps.TryGet(player.MapId, out MapSpec? spec)
            || !_maps.TryGet(player.MapId, out MapDefinition? map))
        {
            return false;
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        return ShopRules.IsAtCounter(spec, MarketRules.MarketCounterId, feetX, feetY, map.Grid.TileSize);
    }

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
        MarketVerdict.TooManyOfItem => MarketStatus.TooManyOfItem,
        _ => MarketStatus.TooManyListings,
    };
}
