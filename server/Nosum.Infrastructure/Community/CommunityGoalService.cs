// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Community;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Goals;
using Nosum.Contracts.Goals.S2C;
using Nosum.Domain.Community;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Community;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Shops;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Community;

/// <summary>
/// <see cref="ICommunityGoalService"/> on top of <see cref="ITransferService"/> (docs/adr/ADR-030). A delivery is one transfer: the items leave the player and the goal's
/// total and the player's contribution change in the same transaction, serialized per goal by an in-process lock (the server is one process, docs/adr/ADR-009) with the
/// total as a concurrency token behind it, so concurrent deliveries add up exactly and a goal can never take more than its target.
/// A goal closes (completed or expired) in a transaction of its own; the rewards are paid afterwards, one transfer per contributor, each guarded by the contribution's
/// <c>PaidCoins</c> being still empty, so a payout happens once however many times settling runs and a crash in between is picked up by the next sweep.
/// </summary>
public sealed class CommunityGoalService : ICommunityGoalService
{
    private const string RewardReason = "community.reward";
    private const string ConsolationReason = "community.consolation";

    private readonly ITransferService _transfers;
    private readonly IUnitOfWorkFactory _units;
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly GameCalendar _calendar;
    private readonly TimeProvider _time;
    private readonly CommunityUnlocks _unlocks;
    private readonly StripedAsyncLock _goalLocks = new(16);
    private readonly StripedAsyncLock _closeLock = new(1);

    /// <summary>Initializes the service.</summary>
    /// <param name="transfers">Moves the delivered items and pays the rewards.</param>
    /// <param name="units">Creates units of work.</param>
    /// <param name="presence">Where players stand (the board needs the position) and the Town group that receives progress pushes.</param>
    /// <param name="maps">The hosted maps (tile size).</param>
    /// <param name="calendar">The server calendar: which season a goal belongs to and when it ends.</param>
    /// <param name="time">The server clock; tests pass a fake one.</param>
    /// <param name="unlocks">The unlock flags; a completed goal adds its flag.</param>
    public CommunityGoalService(ITransferService transfers, IUnitOfWorkFactory units, IMapPresence presence, IMapCatalog maps, GameCalendar calendar, TimeProvider time, CommunityUnlocks unlocks)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(unlocks);

        _transfers = transfers;
        _units = units;
        _presence = presence;
        _maps = maps;
        _calendar = calendar;
        _time = time;
        _unlocks = unlocks;
    }

    /// <inheritdoc/>
    public async Task<GoalsView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        _ = await this.CloseEndedSeasonsAsync(cancellationToken).ConfigureAwait(false);

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        int season = _calendar.SeasonNumberAt(nowMs);
        Season current = _calendar.SeasonAt(nowMs);
        List<GoalView> goals = [];
        foreach (CommunityGoalDefinition goal in CommunityGoalCatalog.All)
        {
            CommunityGoalProgress? row = await this.FindCompletedAsync(goal.Id, cancellationToken).ConfigureAwait(false);
            if (row is null && goal.Season == current)
            {
                row = await this.OpenRowAsync(goal.Id, season, cancellationToken).ConfigureAwait(false);
            }

            if (row is not null)
            {
                goals.Add(await this.ViewAsync(row, playerId, cancellationToken).ConfigureAwait(false));
            }
        }

        return new GoalsView(nowMs, _calendar.NextSeasonStartMs(nowMs), goals);
    }

    /// <inheritdoc/>
    public async Task<DeliverOutcome> DeliverAsync(IConnection connection, Guid playerId, int goalId, int quantity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        DeliverVerdict verdict = CommunityGoalRules.Validate(goalId, quantity);
        if (verdict != DeliverVerdict.Ok)
        {
            return new DeliverOutcome(verdict == DeliverVerdict.UnknownGoal ? GoalStatus.UnknownGoal : GoalStatus.InvalidQuantity, 0);
        }

        _ = CommunityGoalCatalog.TryGet(goalId, out CommunityGoalDefinition? goal);
        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        if (goal!.Season != _calendar.SeasonAt(nowMs))
        {
            return new DeliverOutcome(GoalStatus.NotThisSeason, 0);
        }

        if (!this.IsAtBoard(connection))
        {
            return new DeliverOutcome(GoalStatus.NotAtBoard, 0);
        }

        _ = await this.CloseEndedSeasonsAsync(cancellationToken).ConfigureAwait(false);

        DeliverOutcome outcome;
        Guid rowId;
        bool completed = false;
        using (await _goalLocks.EnterAsync(goalId, cancellationToken).ConfigureAwait(false))
        {
            if (await this.FindCompletedAsync(goalId, cancellationToken).ConfigureAwait(false) is not null)
            {
                return new DeliverOutcome(GoalStatus.Closed, 0);
            }

            CommunityGoalProgress row = await this.OpenRowAsync(goalId, _calendar.SeasonNumberAt(nowMs), cancellationToken).ConfigureAwait(false);
            rowId = row.Id;
            if (row.Status != GoalStanding.Active)
            {
                return new DeliverOutcome(GoalStatus.Closed, 0);
            }

            // Only what still fits is taken: the last delivery of a goal is the remainder, never more.
            long take = Math.Min(quantity, CommunityGoalRules.Remaining(goal, row.Total));
            if (take < 1)
            {
                return new DeliverOutcome(GoalStatus.Closed, 0);
            }

            long expectedTotal = row.Total;
            TransferStatus status = await _transfers.ExecuteAsync(
                [new TransferLeg(TransferParty.Player(playerId), goal.ItemId, -(int)take)],
                "community.deliver",
                async (unit, token) =>
                {
                    CommunityGoalProgress? tracked = await unit.Repository<CommunityGoalProgress>().FindAsync(rowId, token).ConfigureAwait(false);
                    if (tracked is null || tracked.Status != GoalStanding.Active || tracked.Total != expectedTotal)
                    {
                        return TransferStatus.Refused;
                    }

                    IRepository<CommunityContribution> contributions = unit.Repository<CommunityContribution>();
                    CommunityContribution? mine = await contributions.FirstOrDefaultAsync(c => c.ProgressId == rowId && c.PlayerId == playerId, token).ConfigureAwait(false);
                    if (mine is null)
                    {
                        contributions.Add(new CommunityContribution { ProgressId = rowId, PlayerId = playerId, Delivered = take });
                    }
                    else
                    {
                        mine.Delivered += take;
                    }

                    tracked.Total += take;
                    if (tracked.Total >= goal.Target)
                    {
                        // Completion is part of the delivery that reached the target: the standing and the unlock flag commit with the items.
                        DateTimeOffset now = _time.GetUtcNow();
                        tracked.Status = GoalStanding.Completed;
                        tracked.ClosedAt = now;
                        completed = true;
                        if (goal.UnlockKey.Length > 0 && !await unit.Repository<CommunityUnlockRecord>().AnyAsync(u => u.Key == goal.UnlockKey, token).ConfigureAwait(false))
                        {
                            unit.Repository<CommunityUnlockRecord>().Add(new CommunityUnlockRecord { Key = goal.UnlockKey, GoalId = goal.Id, UnlockedAt = now });
                        }
                    }

                    return TransferStatus.Ok;
                },
                cancellationToken).ConfigureAwait(false);

            outcome = status switch
            {
                TransferStatus.Ok => new DeliverOutcome(GoalStatus.Ok, (int)take),
                TransferStatus.NotEnoughItems => new DeliverOutcome(GoalStatus.NotEnough, 0),
                _ => new DeliverOutcome(GoalStatus.Conflict, 0),
            };
            if (outcome.Status == GoalStatus.Ok && completed && goal.UnlockKey.Length > 0)
            {
                _unlocks.Add(goal.UnlockKey);
            }
        }

        if (outcome.Status == GoalStatus.Ok)
        {
            await this.PushProgressAsync(rowId, cancellationToken).ConfigureAwait(false);
            if (completed)
            {
                _ = await this.SettleAsync(rowId, cancellationToken).ConfigureAwait(false);
            }
        }

        return outcome;
    }

    /// <inheritdoc/>
    public async Task<int> CloseEndedSeasonsAsync(CancellationToken cancellationToken = default)
    {
        int season = _calendar.SeasonNumberAt(_time.GetUtcNow().ToUnixTimeMilliseconds());
        List<Guid> expired = [];
        using (await _closeLock.EnterAsync(0, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _units.Create();
            await using (unit.ConfigureAwait(false))
            {
                List<CommunityGoalProgress> open = await unit.Repository<CommunityGoalProgress>().ListAsync(g => g.Status == GoalStanding.Active && g.SeasonNumber < season, cancellationToken).ConfigureAwait(false);
                DateTimeOffset now = _time.GetUtcNow();
                foreach (CommunityGoalProgress row in open)
                {
                    row.Status = GoalStanding.Expired;
                    row.ClosedAt = now;
                    expired.Add(row.Id);
                }

                if (open.Count > 0)
                {
                    try
                    {
                        _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (unit.IsConcurrencyConflict(ex))
                    {
                        // A delivery changed one of them at the same moment; the next sweep expires what is still open.
                        expired.Clear();
                    }
                }
            }
        }

        foreach (Guid id in expired)
        {
            await this.PushProgressAsync(id, cancellationToken).ConfigureAwait(false);
            _ = await this.SettleAsync(id, cancellationToken).ConfigureAwait(false);
        }

        return expired.Count;
    }

    /// <inheritdoc/>
    public async Task<int> SettlePendingAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> unpaid;
        IUnitOfWork unit = _units.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<CommunityContribution> rows = await unit.Repository<CommunityContribution>().ListAsync(c => c.PaidCoins == null, cancellationToken).ConfigureAwait(false);
            unpaid = [.. rows.Select(c => c.ProgressId).Distinct()];
        }

        int paid = 0;
        foreach (Guid progressId in unpaid)
        {
            paid += await this.SettleAsync(progressId, cancellationToken).ConfigureAwait(false);
        }

        return paid;
    }

    // Pays every unpaid contribution of a closed goal. Nothing is paid for a goal that is still open.
    private async Task<int> SettleAsync(Guid progressId, CancellationToken cancellationToken)
    {
        CommunityGoalProgress? row;
        List<CommunityContribution> owed;
        IUnitOfWork unit = _units.Create();
        await using (unit.ConfigureAwait(false))
        {
            row = await unit.Repository<CommunityGoalProgress>().FindAsync(progressId, cancellationToken).ConfigureAwait(false);
            if (row is null || row.Status == GoalStanding.Active)
            {
                return 0;
            }

            owed = await unit.Repository<CommunityContribution>().ListAsync(c => c.ProgressId == progressId && c.PaidCoins == null, cancellationToken).ConfigureAwait(false);
        }

        if (!CommunityGoalCatalog.TryGet(row.GoalId, out CommunityGoalDefinition? goal))
        {
            return 0;
        }

        bool completed = row.Status == GoalStanding.Completed;
        int paid = 0;
        foreach (CommunityContribution contribution in owed)
        {
            long coins = completed ? CommunityGoalRules.RewardShare(goal, contribution.Delivered, row.Total) : CommunityGoalRules.Consolation(goal, contribution.Delivered);
            if (await this.PayAsync(contribution.Id, contribution.PlayerId, coins, completed ? RewardReason : ConsolationReason, cancellationToken).ConfigureAwait(false))
            {
                paid++;
            }
        }

        return paid;
    }

    // The one call site that pays a contributor: coins now, through the transfer service. When a mail service exists, this is the line that sends the coins by mail instead
    // (docs/adr/ADR-030 D1, ADR-029); the contribution's PaidCoins guard stays either way.
    private async Task<bool> PayAsync(Guid contributionId, Guid playerId, long coins, string reason, CancellationToken cancellationToken)
    {
        TransferStatus status = await _transfers.ExecuteAsync(
            [new TransferLeg(TransferParty.Player(playerId), 0, 0, coins)],
            reason,
            async (unit, token) =>
            {
                CommunityContribution? tracked = await unit.Repository<CommunityContribution>().FindAsync(contributionId, token).ConfigureAwait(false);
                if (tracked is null || tracked.PaidCoins is not null)
                {
                    return TransferStatus.Refused;
                }

                tracked.PaidCoins = coins;
                return TransferStatus.Ok;
            },
            cancellationToken).ConfigureAwait(false);
        return status == TransferStatus.Ok;
    }

    // The newest row of a goal in a season, created when the season's first look at the goal comes. A lost race on the unique index reads the winner's row.
    private async Task<CommunityGoalProgress> OpenRowAsync(int goalId, int season, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _units.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<CommunityGoalProgress> rows = unit.Repository<CommunityGoalProgress>();
            CommunityGoalProgress? found = await rows.FirstOrDefaultAsync(g => g.GoalId == goalId && g.SeasonNumber == season, cancellationToken).ConfigureAwait(false);
            if (found is not null)
            {
                return found;
            }

            CommunityGoalProgress created = new() { GoalId = goalId, SeasonNumber = season, Status = GoalStanding.Active, CreatedAt = _time.GetUtcNow() };
            rows.Add(created);
            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return created;
            }
            catch (Exception ex) when (unit.IsUniqueViolation(ex))
            {
                // Fall through to read the row another request created.
            }
        }

        IUnitOfWork again = _units.Create();
        await using (again.ConfigureAwait(false))
        {
            return await again.Repository<CommunityGoalProgress>().FirstOrDefaultAsync(g => g.GoalId == goalId && g.SeasonNumber == season, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The community goal row vanished after a unique violation.");
        }
    }

    private async Task<CommunityGoalProgress?> FindCompletedAsync(int goalId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _units.Create();
        await using (unit.ConfigureAwait(false))
        {
            return await unit.Repository<CommunityGoalProgress>().FirstOrDefaultAsync(g => g.GoalId == goalId && g.Status == GoalStanding.Completed, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<GoalView> ViewAsync(CommunityGoalProgress row, Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _units.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<CommunityContribution> contributions = unit.Repository<CommunityContribution>();
            CommunityContribution? mine = await contributions.FirstOrDefaultAsync(c => c.ProgressId == row.Id && c.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
            int contributors = await contributions.CountAsync(c => c.ProgressId == row.Id, cancellationToken).ConfigureAwait(false);
            return new GoalView(row.GoalId, row.SeasonNumber, row.Total, ToState(row.Status), mine?.Delivered ?? 0, contributors);
        }
    }

    // Tells everyone in the Town the new total. Best effort: the board also reads the numbers when it opens.
    private async Task PushProgressAsync(Guid progressId, CancellationToken cancellationToken)
    {
        CommunityGoalProgress? row;
        int contributors;
        IUnitOfWork unit = _units.Create();
        await using (unit.ConfigureAwait(false))
        {
            row = await unit.Repository<CommunityGoalProgress>().FindAsync(progressId, cancellationToken).ConfigureAwait(false);
            contributors = await unit.Repository<CommunityContribution>().CountAsync(c => c.ProgressId == progressId, cancellationToken).ConfigureAwait(false);
        }

        if (row is null)
        {
            return;
        }

        using PacketScope<GoalProgress> lease = PacketFactory<GoalProgress>.Acquire();
        lease.Value.GoalId = row.GoalId;
        lease.Value.SeasonNumber = row.SeasonNumber;
        lease.Value.Total = row.Total;
        lease.Value.State = ToState(row.Status);
        lease.Value.Contributors = contributors;
        await _presence.MulticastAsync(GameMaps.Town, lease.Value, null, cancellationToken).ConfigureAwait(false);
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

    private static GoalState ToState(GoalStanding status) => status switch
    {
        GoalStanding.Completed => GoalState.Completed,
        GoalStanding.Expired => GoalState.Expired,
        _ => GoalState.Active,
    };
}
