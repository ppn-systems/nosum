// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Blacksmith;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Quests;
using Nosum.Contracts.Shop;
using Nosum.Domain.Blacksmith;
using Nosum.Gameplay.Blacksmith;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Quests;
using Nosum.Infrastructure.Shops;

namespace Nosum.Infrastructure.Blacksmith;

/// <summary>
/// <see cref="IBlacksmithService"/> (docs/adr/ADR-026 D2). An upgrade is one transfer: the old tool, the bars and the coins leave the player and a
/// <see cref="ToolUpgrade"/> row is written in the same transaction; collecting is one transfer too: the row is deleted and the new tool arrives. The
/// day counts come from the server calendar and the (possibly dev-moved) clock, so two game days are the same two days everywhere.
/// </summary>
public sealed class BlacksmithService : IBlacksmithService
{
    private readonly ITransferService _transfers;
    private readonly IInventoryService _inventory;
    private readonly IQuestService? _quests;
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IToolState _tools;
    private readonly GameCalendar _calendar;
    private readonly TimeProvider _time;

    /// <summary>Initializes the service.</summary>
    /// <param name="transfers">Moves tool, bars and coins atomically.</param>
    /// <param name="inventory">Reads what the player carries, to tell a missing tool from missing bars.</param>
    /// <param name="unitOfWork">Reads the job rows.</param>
    /// <param name="presence">Where players stand.</param>
    /// <param name="maps">The hosted maps (tile size).</param>
    /// <param name="tools">Fills a collected watering can to its new capacity.</param>
    /// <param name="calendar">The server calendar.</param>
    /// <param name="time">The server clock; tests pass a fake one.</param>
    /// <param name="quests">Hears about the player actions the tutorial chain counts; <see langword="null"/> to hear nothing.</param>
    public BlacksmithService(ITransferService transfers, IInventoryService inventory, IUnitOfWorkFactory unitOfWork, IMapPresence presence, IMapCatalog maps, IToolState tools, GameCalendar calendar, TimeProvider time, IQuestService? quests = null)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);

        _transfers = transfers;
        _inventory = inventory;
        _unitOfWork = unitOfWork;
        _presence = presence;
        _maps = maps;
        _tools = tools;
        _calendar = calendar;
        _time = time;
        _quests = quests;
    }

    /// <inheritdoc/>
    public async Task<BlacksmithView> GetAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        long today = this.Today();
        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return new BlacksmithView(BlacksmithStatus.Ok, 0, 0, today);
        }

        return await this.ViewAsync(player.AccountId, BlacksmithStatus.Ok, today, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BlacksmithView> UpgradeAsync(IConnection connection, int toolItemId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        long today = this.Today();
        if (!_presence.TryGet(connection, out PlayerPresence? player) || !this.IsAtCounter(player))
        {
            return new BlacksmithView(BlacksmithStatus.NotAtCounter, 0, 0, today);
        }

        Guid playerId = player.AccountId;
        if (!BlacksmithRules.TryQuote(toolItemId, out UpgradeQuote? found))
        {
            return await this.ViewAsync(playerId, BlacksmithStatus.NotUpgradable, today, cancellationToken).ConfigureAwait(false);
        }

        UpgradeQuote quote = found.Value;
        if (await this.FindAsync(playerId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return await this.ViewAsync(playerId, BlacksmithStatus.Busy, today, cancellationToken).ConfigureAwait(false);
        }

        // A missing tool and missing bars are told apart before the transfer, which only knows "not enough items".
        InventoryView bag = await _inventory.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        if (Count(bag, quote.FromItemId) < 1)
        {
            return await this.ViewAsync(playerId, BlacksmithStatus.NoTool, today, cancellationToken).ConfigureAwait(false);
        }

        if (Count(bag, quote.BarItemId) < quote.BarCount)
        {
            return await this.ViewAsync(playerId, BlacksmithStatus.NotEnoughBars, today, cancellationToken).ConfigureAwait(false);
        }

        TransferStatus status = await _transfers.ExecuteAsync(
            [
                new TransferLeg(TransferParty.Player(playerId), quote.FromItemId, -1, -quote.Coins),
                new TransferLeg(TransferParty.Player(playerId), quote.BarItemId, -quote.BarCount),
            ],
            "blacksmith.upgrade",
            async (unit, token) =>
            {
                // One job at a time, decided inside the transaction too: two requests at once cannot both be accepted.
                IRepository<ToolUpgrade> jobs = unit.Repository<ToolUpgrade>();
                if (await jobs.AnyAsync(j => j.PlayerId == playerId, token).ConfigureAwait(false))
                {
                    return TransferStatus.Refused;
                }

                jobs.Add(new ToolUpgrade { PlayerId = playerId, ToolItemId = quote.ToItemId, ReadyDay = BlacksmithRules.ReadyDay(today), StartedAt = _time.GetUtcNow() });
                return TransferStatus.Ok;
            },
            cancellationToken).ConfigureAwait(false);

        return await this.ViewAsync(playerId, status switch
        {
            TransferStatus.Ok => BlacksmithStatus.Ok,
            TransferStatus.NotEnoughCoins => BlacksmithStatus.NotEnoughCoins,
            TransferStatus.NotEnoughItems => BlacksmithStatus.NoTool,
            TransferStatus.Refused => BlacksmithStatus.Busy,
            _ => BlacksmithStatus.Conflict,
        }, today, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BlacksmithView> CollectAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        long today = this.Today();
        if (!_presence.TryGet(connection, out PlayerPresence? player) || !this.IsAtCounter(player))
        {
            return new BlacksmithView(BlacksmithStatus.NotAtCounter, 0, 0, today);
        }

        Guid playerId = player.AccountId;
        ToolUpgrade? job = await this.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return await this.ViewAsync(playerId, BlacksmithStatus.NothingToCollect, today, cancellationToken).ConfigureAwait(false);
        }

        if (!BlacksmithRules.IsReady(job.ReadyDay, today))
        {
            return await this.ViewAsync(playerId, BlacksmithStatus.NotReady, today, cancellationToken).ConfigureAwait(false);
        }

        int toolItemId = job.ToolItemId;
        TransferStatus status = await _transfers.ExecuteAsync(
            [new TransferLeg(TransferParty.Player(playerId), toolItemId, 1)],
            "blacksmith.collect",
            async (unit, token) =>
            {
                IRepository<ToolUpgrade> jobs = unit.Repository<ToolUpgrade>();
                ToolUpgrade? row = await jobs.FindAsync(playerId, token).ConfigureAwait(false);
                if (row is null || row.ToolItemId != toolItemId)
                {
                    return TransferStatus.Refused;
                }

                jobs.Remove(row);
                return TransferStatus.Ok;
            },
            cancellationToken).ConfigureAwait(false);

        if (status == TransferStatus.Ok && ItemCatalog.TryGet(toolItemId, out ItemDefinition? tool) && tool.Tool == ToolKind.WateringCan)
        {
            // A new can comes full, to its new capacity.
            _ = await _tools.RefillAsync(playerId, ToolRules.WateringCanCapacity(tool.Tier), cancellationToken).ConfigureAwait(false);
        }

        if (status == TransferStatus.Ok)
        {
            await _quests.TryReportAsync(playerId, new QuestEvent(QuestEventKind.ToolUpgraded, toolItemId), cancellationToken).ConfigureAwait(false);
        }

        return await this.ViewAsync(playerId, status switch
        {
            TransferStatus.Ok => BlacksmithStatus.Ok,
            TransferStatus.NoSpace => BlacksmithStatus.NoSpace,
            TransferStatus.Refused => BlacksmithStatus.NothingToCollect,
            _ => BlacksmithStatus.Conflict,
        }, today, cancellationToken).ConfigureAwait(false);
    }

    private static int Count(InventoryView bag, int itemId)
    {
        int count = 0;
        foreach (InventorySlotView slot in bag.Slots)
        {
            if (slot.ItemId == itemId)
            {
                count += slot.Quantity;
            }
        }

        return count;
    }

    private long Today() => _calendar.DayNumberAt(_time.GetUtcNow().ToUnixTimeMilliseconds());

    private async Task<ToolUpgrade?> FindAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            return await unit.Repository<ToolUpgrade>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<BlacksmithView> ViewAsync(Guid playerId, BlacksmithStatus status, long today, CancellationToken cancellationToken)
    {
        ToolUpgrade? job = await this.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
        return job is null ? new BlacksmithView(status, 0, 0, today) : new BlacksmithView(status, job.ToolItemId, job.ReadyDay, today);
    }

    // The position is the server's own record, never a value from the packet.
    private bool IsAtCounter(PlayerPresence player) => CounterReach.IsAtCounter(_maps, player, ShopCatalog.Blacksmith);
}
