// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Quests;
using Nosum.Abstractions.Requests;
using Nosum.Contracts.Requests;
using Nosum.Domain.Requests;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Quests;
using Nosum.Gameplay.Requests;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Concurrency;
using Nosum.Infrastructure.Quests;

namespace Nosum.Infrastructure.Requests;

/// <summary>
/// <see cref="IRequestService"/> on top of <see cref="ITransferService"/> (docs/adr/ADR-036). The requests of a game day are rows written once from the day's
/// deterministic roll (<see cref="RequestRules.Roll"/>), so a restart or two racing readers write the same rows. A delivery is one transfer: the items leave the
/// player, the reward arrives and the fill row is added in the same transaction; the fill's primary key (day, slot, player) is what makes a second fill impossible,
/// and one player's deliveries are serialized in process so that the check and the write are not interleaved.
/// </summary>
public sealed class RequestService : IRequestService
{
    /// <summary>How many game days of requests are kept; older rows (and their fills) are deleted when a new day is written.</summary>
    public const int RetentionDays = 14;

    private const string DeliverReason = "request.deliver";

    private readonly ITransferService _transfers;
    private readonly IQuestService? _quests;
    private readonly IUnitOfWorkFactory _units;
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly GameCalendar _calendar;
    private readonly TimeProvider _time;
    private readonly StripedAsyncLock _playerLocks = new();
    private readonly StripedAsyncLock _dayLock = new(1);
    private int _ensuredDay;

    /// <summary>Initializes the service.</summary>
    /// <param name="transfers">Moves the delivered items and pays the reward.</param>
    /// <param name="units">Creates units of work.</param>
    /// <param name="presence">Where players stand (the board needs the position).</param>
    /// <param name="maps">The hosted maps (tile size).</param>
    /// <param name="calendar">The server calendar: the game day and the season a day falls in.</param>
    /// <param name="time">The server clock; tests pass a fake one.</param>
    /// <param name="quests">Hears about the player actions the tutorial chain counts; <see langword="null"/> to hear nothing.</param>
    public RequestService(ITransferService transfers, IUnitOfWorkFactory units, IMapPresence presence, IMapCatalog maps, GameCalendar calendar, TimeProvider time, IQuestService? quests = null)
    {
        _quests = quests;
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);

        _transfers = transfers;
        _units = units;
        _presence = presence;
        _maps = maps;
        _calendar = calendar;
        _time = time;
    }

    /// <inheritdoc/>
    public async Task<RequestsView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        int day = _calendar.DayNumberAt(nowMs);
        await this.EnsureDayAsync(day, cancellationToken).ConfigureAwait(false);

        IUnitOfWork unit = _units.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<DailyRequest> rows = await unit.Repository<DailyRequest>().ListAsync(r => r.Day == day, cancellationToken).ConfigureAwait(false);
            List<RequestFill> fills = await unit.Repository<RequestFill>().ListAsync(f => f.Day == day && f.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
            rows.Sort(static (a, b) => a.Slot.CompareTo(b.Slot));
            List<RequestView> requests = new(rows.Count);
            foreach (DailyRequest row in rows)
            {
                requests.Add(new RequestView(row.Slot, row.ItemId, row.Quantity, row.RewardCoins, fills.Exists(f => f.Slot == row.Slot)));
            }

            long earlyUntilMs = _calendar.DayStartMs(day) + RequestRules.EarlyWindowMs(_calendar.DayLengthMs);
            return new RequestsView(nowMs, day, _calendar.NextDayStartMs(nowMs), requests, earlyUntilMs, RequestRules.EarlyBonusPercent);
        }
    }

    /// <inheritdoc/>
    public async Task<RequestOutcome> DeliverAsync(IConnection connection, Guid playerId, int day, int slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!RequestRules.IsValidSlot(slot))
        {
            return new RequestOutcome(RequestStatus.UnknownRequest, 0);
        }

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        int today = _calendar.DayNumberAt(nowMs);
        if (day != today)
        {
            return new RequestOutcome(RequestStatus.NotToday, 0);
        }

        if (!this.IsAtBoard(connection))
        {
            return new RequestOutcome(RequestStatus.NotAtBoard, 0);
        }

        await this.EnsureDayAsync(today, cancellationToken).ConfigureAwait(false);

        using (await _playerLocks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            DailyRequest? request;
            bool filled;
            IUnitOfWork unit = _units.Create();
            await using (unit.ConfigureAwait(false))
            {
                request = await unit.Repository<DailyRequest>().FirstOrDefaultAsync(r => r.Day == today && r.Slot == slot, cancellationToken).ConfigureAwait(false);
                filled = await unit.Repository<RequestFill>().AnyAsync(f => f.Day == today && f.Slot == slot && f.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
            }

            if (request is null)
            {
                return new RequestOutcome(RequestStatus.UnknownRequest, 0);
            }

            if (filled)
            {
                return new RequestOutcome(RequestStatus.AlreadyFilled, 0);
            }

            bool early = RequestRules.IsEarly(nowMs - _calendar.DayStartMs(today), _calendar.DayLengthMs);
            long reward = RequestRules.PaidFor(request.RewardCoins, early);
            TransferStatus status = await _transfers.ExecuteAsync(
                [new TransferLeg(TransferParty.Player(playerId), request.ItemId, -request.Quantity, reward)],
                DeliverReason,
                async (inner, token) =>
                {
                    // The same check inside the transaction, so a fill that raced past the one above still loses.
                    if (await inner.Repository<RequestFill>().AnyAsync(f => f.Day == today && f.Slot == slot && f.PlayerId == playerId, token).ConfigureAwait(false))
                    {
                        return TransferStatus.Refused;
                    }

                    inner.Repository<RequestFill>().Add(new RequestFill { Day = today, Slot = slot, PlayerId = playerId, RewardCoins = reward, FilledAt = _time.GetUtcNow() });
                    return TransferStatus.Ok;
                },
                cancellationToken).ConfigureAwait(false);

            if (status == TransferStatus.Ok)
            {
                await _quests.TryReportAsync(playerId, new QuestEvent(QuestEventKind.RequestFilled), cancellationToken).ConfigureAwait(false);
            }

            return status switch
            {
                TransferStatus.Ok => new RequestOutcome(RequestStatus.Ok, reward, early),
                TransferStatus.NotEnoughItems => new RequestOutcome(RequestStatus.NotEnough, 0),
                TransferStatus.TooBig => new RequestOutcome(RequestStatus.PurseFull, 0),
                TransferStatus.Refused => new RequestOutcome(RequestStatus.AlreadyFilled, 0),
                _ => new RequestOutcome(RequestStatus.Conflict, 0),
            };
        }
    }

    /// <inheritdoc/>
    public async Task EnsureDayAsync(int day, CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _ensuredDay) >= day)
        {
            return;
        }

        using (await _dayLock.EnterAsync(0, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _units.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<DailyRequest> requests = unit.Repository<DailyRequest>();
                if (!await requests.AnyAsync(r => r.Day == day, cancellationToken).ConfigureAwait(false))
                {
                    Season season = _calendar.SeasonAt(_calendar.DayStartMs(day));
                    DateTimeOffset now = _time.GetUtcNow();
                    foreach (RequestTemplate template in RequestRules.Roll(day, season))
                    {
                        requests.Add(new DailyRequest { Day = day, Slot = template.Slot, ItemId = template.ItemId, Quantity = template.Quantity, RewardCoins = template.RewardCoins, CreatedAt = now });
                    }

                    // Old days go with the new one; their fills are deleted by the foreign key.
                    int oldest = day - RetentionDays;
                    foreach (DailyRequest old in await requests.ListAsync(r => r.Day < oldest, cancellationToken).ConfigureAwait(false))
                    {
                        requests.Remove(old);
                    }

                    try
                    {
                        _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (unit.IsUniqueViolation(ex))
                    {
                        // Another process wrote the same rows first; the roll is deterministic, so they are the ones this call would have written.
                    }
                }
            }

            Volatile.Write(ref _ensuredDay, Math.Max(Volatile.Read(ref _ensuredDay), day));
        }
    }

    // The position is the server's own record, never a value from the packet.
    private bool IsAtBoard(IConnection connection)
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

        return ShopRules.IsAtCounter(spec, CommunityGoalCatalog.BoardCounterId, feetX, feetY, map.Grid.TileSize);
    }
}
