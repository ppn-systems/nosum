// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.S2C;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Domain.Inventory;
using Nosum.Domain.Placeables;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;

namespace Nosum.Infrastructure.Coops;

/// <summary>
/// <see cref="ICoopService"/> on PostgreSQL. Membership changes and warehouse moves are serialized by one gate (co-op traffic is
/// low); a warehouse move is a <see cref="ITransferService"/> transfer with the audit line enlisted, so items, stock and log change
/// together or not at all. A player belongs to one co-op (unique index). Who may withdraw what is <see cref="CoopRules"/>.
/// </summary>
public sealed class CoopService : ICoopService, IDisposable
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly ITransferService _transfers;
    private readonly IMapPresence _presence;
    private readonly ISessionRegistry _sessions;
    private readonly IPlayerNotifier _notifier;
    private readonly INicknameDirectory _nicknames;
    private readonly IFarmService _farm;
    private readonly ICoopLandEviction _eviction;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="transfers">Moves items between inventories and the warehouse.</param>
    /// <param name="presence">Who is on which map (an invitation goes to a player on the inviter's map).</param>
    /// <param name="sessions">Who is connected.</param>
    /// <param name="notifier">Pushes snapshots to online members.</param>
    /// <param name="nicknames">Resolves member nicknames.</param>
    /// <param name="farm">Told to forget the land of a dissolved co-op.</param>
    /// <param name="eviction">Takes a member off co-op land when they leave or are removed (docs/adr/ADR-020).</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    public CoopService(IUnitOfWorkFactory unitOfWork, ITransferService transfers, IMapPresence presence, ISessionRegistry sessions, IPlayerNotifier notifier, INicknameDirectory nicknames, IFarmService farm, ICoopLandEviction eviction, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(nicknames);
        ArgumentNullException.ThrowIfNull(farm);
        ArgumentNullException.ThrowIfNull(eviction);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _transfers = transfers;
        _presence = presence;
        _sessions = sessions;
        _notifier = notifier;
        _nicknames = nicknames;
        _farm = farm;
        _eviction = eviction;
        _time = time;
    }

    /// <inheritdoc/>
    public event Func<Guid, ValueTask>? MembershipEnded;

    /// <inheritdoc/>
    public async Task<CoopView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            CoopMember? me = await unit.Repository<CoopMember>().FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
            if (me is null)
            {
                List<CoopInvite> invites = await unit.Repository<CoopInvite>().ListAsync(i => i.PlayerId == playerId && i.ExpiresAt > now, cancellationToken).ConfigureAwait(false);
                List<CoopInviteView> views = [];
                foreach (CoopInvite invite in invites)
                {
                    Coop? coop = await unit.Repository<Coop>().FindAsync(invite.CoopId, cancellationToken).ConfigureAwait(false);
                    if (coop is not null)
                    {
                        CoopMember? founder = await unit.Repository<CoopMember>().FirstOrDefaultAsync(m => m.CoopId == coop.Id && m.Role == CoopRole.Founder, cancellationToken).ConfigureAwait(false);
                        string inviter = founder is null ? string.Empty : await _nicknames.GetAsync(founder.PlayerId, cancellationToken).ConfigureAwait(false);
                        views.Add(new CoopInviteView(invite.Id, coop.Name, inviter));
                    }
                }

                return new CoopView(false, string.Empty, CoopRole.None, [], [], [], views, now);
            }

            Coop? mine = await unit.Repository<Coop>().FindAsync(me.CoopId, cancellationToken).ConfigureAwait(false);
            List<CoopMember> members = await unit.Repository<CoopMember>().ListAsync(m => m.CoopId == me.CoopId, cancellationToken).ConfigureAwait(false);
            HashSet<Guid> memberIds = [.. members.Select(m => m.Id)];
            IReadOnlyDictionary<Guid, string> names = await _nicknames.GetManyAsync(members.Select(m => m.PlayerId), cancellationToken).ConfigureAwait(false);
            List<CoopMemberView> memberViews = [.. members
                .OrderBy(m => m.JoinedAt)
                .Select(m => new CoopMemberView(m.Id, m.Role, m.Id == me.Id, _sessions.GetActive(m.PlayerId) is not null, names.GetValueOrDefault(m.PlayerId, string.Empty)))];

            List<CoopLogEntry> mineLog = await unit.Repository<CoopLogEntry>().ListAsync(e => e.CoopId == me.CoopId && e.MemberId == me.Id, cancellationToken).ConfigureAwait(false);
            Dictionary<int, long> contributed = mineLog.Where(e => CoopLand.IsWarehouseReason(e.Reason)).GroupBy(e => e.ItemId).ToDictionary(g => g.Key, g => g.Sum(e => (long)e.Delta));
            List<CoopStockItem> stock = await unit.Repository<CoopStockItem>().ListAsync(s => s.CoopId == me.CoopId, cancellationToken).ConfigureAwait(false);
            List<CoopStockView> stockViews = [.. stock
                .OrderBy(s => s.ItemId)
                .Select(s => new CoopStockView(s.ItemId, s.Quantity, CoopRules.Withdrawable(me.Role == CoopRole.Founder, s.Quantity, contributed.GetValueOrDefault(s.ItemId))))];

            List<CoopLogEntry> recent = await unit.Repository<CoopLogEntry>().PageAsync(e => e.CoopId == me.CoopId && (e.Reason == "coop.deposit" || e.Reason == "coop.withdraw"), e => e.OccurredAt, 0, CoopRules.RecentLogLines, cancellationToken).ConfigureAwait(false);
            List<CoopLogView> logViews = [.. recent.Select(e => new CoopLogView(memberIds.Contains(e.MemberId) ? e.MemberId : Guid.Empty, e.ItemId, e.Delta, e.OccurredAt))];

            return new CoopView(true, mine?.Name ?? string.Empty, me.Role, memberViews, stockViews, logViews, [], now);
        }
    }

    /// <inheritdoc/>
    public async Task<CoopStatus> CreateAsync(Guid playerId, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (CoopRules.ValidateName(name) != CoopVerdict.Ok)
        {
            return CoopStatus.InvalidName;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                if (await unit.Repository<CoopMember>().AnyAsync(m => m.PlayerId == playerId, cancellationToken).ConfigureAwait(false))
                {
                    return CoopStatus.AlreadyInCoop;
                }

                Coop coop = new() { Name = name.Trim(), CreatedAt = now };
                unit.Repository<Coop>().Add(coop);
                unit.Repository<CoopMember>().Add(new CoopMember { CoopId = coop.Id, PlayerId = playerId, Role = CoopRole.Founder, JoinedAt = now });
                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (unit.IsUniqueViolation(ex))
                {
                    return CoopStatus.AlreadyInCoop;
                }
            }

            await this.PushToAsync([playerId], cancellationToken).ConfigureAwait(false);
            return CoopStatus.Ok;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<CoopStatus> InviteAsync(IConnection connection, Guid playerId, uint targetMapPlayerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        PlayerPresence? target = null;
        if (_presence.TryGet(connection, out PlayerPresence? me))
        {
            List<PlayerPresence> players = [];
            _presence.CopyPlayers(me.Key, players);
            target = players.Find(p => p.PlayerId == targetMapPlayerId);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                CoopMember? member = await unit.Repository<CoopMember>().FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
                if (member is null)
                {
                    return CoopStatus.NotInCoop;
                }

                if (member.Role != CoopRole.Founder)
                {
                    return CoopStatus.NotFounder;
                }

                if (target is null || target.AccountId == playerId)
                {
                    return CoopStatus.TargetNotFound;
                }

                Guid invitee = target.AccountId;
                if (await unit.Repository<CoopMember>().AnyAsync(m => m.PlayerId == invitee, cancellationToken).ConfigureAwait(false))
                {
                    return CoopStatus.TargetInCoop;
                }

                IRepository<CoopInvite> invites = unit.Repository<CoopInvite>();
                CoopInvite? existing = await invites.FirstOrDefaultAsync(i => i.CoopId == member.CoopId && i.PlayerId == invitee, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (existing.ExpiresAt > now)
                    {
                        return CoopStatus.AlreadyInvited;
                    }

                    invites.Remove(existing);
                }

                invites.Add(new CoopInvite { CoopId = member.CoopId, PlayerId = invitee, CreatedAt = now, ExpiresAt = now.AddHours(CoopRules.InviteHours) });
                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (unit.IsUniqueViolation(ex))
                {
                    return CoopStatus.AlreadyInvited;
                }

                await this.PushToAsync([invitee], cancellationToken).ConfigureAwait(false);
                return CoopStatus.Ok;
            }
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<CoopStatus> RespondAsync(Guid playerId, Guid inviteId, bool accept, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            List<Guid> toPush = [playerId];
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<CoopInvite> invites = unit.Repository<CoopInvite>();
                CoopInvite? invite = await invites.FirstOrDefaultAsync(i => i.Id == inviteId && i.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
                if (invite is null || invite.ExpiresAt <= now)
                {
                    return CoopStatus.InviteNotFound;
                }

                if (!accept)
                {
                    invites.Remove(invite);
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    if (await unit.Repository<CoopMember>().AnyAsync(m => m.PlayerId == playerId, cancellationToken).ConfigureAwait(false))
                    {
                        return CoopStatus.AlreadyInCoop;
                    }

                    foreach (CoopInvite open in await invites.ListAsync(i => i.PlayerId == playerId, cancellationToken).ConfigureAwait(false))
                    {
                        invites.Remove(open);
                    }

                    unit.Repository<CoopMember>().Add(new CoopMember { CoopId = invite.CoopId, PlayerId = playerId, Role = CoopRole.Member, JoinedAt = now });
                    try
                    {
                        _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (unit.IsUniqueViolation(ex))
                    {
                        return CoopStatus.AlreadyInCoop;
                    }

                    toPush.AddRange(await this.MemberPlayersAsync(unit, invite.CoopId, cancellationToken).ConfigureAwait(false));
                }
            }

            await this.PushToAsync(toPush, cancellationToken).ConfigureAwait(false);
            return CoopStatus.Ok;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<CoopStatus> LeaveAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<Guid> toPush = [playerId];
            List<MapInstance> dissolvedLand = [];
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<CoopMember> memberRepository = unit.Repository<CoopMember>();
                CoopMember? me = await memberRepository.FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
                if (me is null)
                {
                    return CoopStatus.NotInCoop;
                }

                List<CoopMember> others = [.. (await memberRepository.ListAsync(m => m.CoopId == me.CoopId && m.Id != me.Id, cancellationToken).ConfigureAwait(false)).OrderBy(m => m.JoinedAt)];
                if (me.Role == CoopRole.Founder)
                {
                    if (others.Count > 0)
                    {
                        // The longest-standing member takes over.
                        others[0].Role = CoopRole.Founder;
                    }
                    else if (await unit.Repository<CoopStockItem>().AnyAsync(s => s.CoopId == me.CoopId, cancellationToken).ConfigureAwait(false)
                        || await this.LandHoldsItemsAsync(unit, me.CoopId, cancellationToken).ConfigureAwait(false))
                    {
                        return CoopStatus.CannotLeave;
                    }
                    else
                    {
                        Coop? coop = await unit.Repository<Coop>().FindAsync(me.CoopId, cancellationToken).ConfigureAwait(false);
                        if (coop is not null)
                        {
                            unit.Repository<Coop>().Remove(coop);
                            dissolvedLand = await this.WipeLandAsync(unit, coop.Id, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                memberRepository.Remove(me);
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                toPush.AddRange(others.Select(m => m.PlayerId));
            }

            foreach (MapInstance land in dissolvedLand)
            {
                _farm.Forget(land);
            }

            await _eviction.EvictAsync(playerId, cancellationToken).ConfigureAwait(false);
            await this.PushToAsync(toPush, cancellationToken).ConfigureAwait(false);
            await this.RaiseMembershipEndedAsync(playerId).ConfigureAwait(false);
            return CoopStatus.Ok;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<CoopStatus> KickAsync(Guid playerId, Guid memberId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<Guid> toPush = [];
            Guid ended;
            Guid removedPlayer;
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                IRepository<CoopMember> members = unit.Repository<CoopMember>();
                CoopMember? me = await members.FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
                if (me is null)
                {
                    return CoopStatus.NotInCoop;
                }

                if (me.Role != CoopRole.Founder)
                {
                    return CoopStatus.NotFounder;
                }

                if (memberId == me.Id)
                {
                    return CoopStatus.SelfKick;
                }

                CoopMember? target = await members.FirstOrDefaultAsync(m => m.Id == memberId && m.CoopId == me.CoopId, cancellationToken).ConfigureAwait(false);
                if (target is null)
                {
                    return CoopStatus.MemberNotFound;
                }

                members.Remove(target);
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                removedPlayer = target.PlayerId;
                toPush.Add(target.PlayerId);
                toPush.AddRange(await this.MemberPlayersAsync(unit, me.CoopId, cancellationToken).ConfigureAwait(false));
                ended = target.PlayerId;
            }

            await _eviction.EvictAsync(removedPlayer, cancellationToken).ConfigureAwait(false);
            await this.PushToAsync(toPush, cancellationToken).ConfigureAwait(false);
            await this.RaiseMembershipEndedAsync(ended).ConfigureAwait(false);
            return CoopStatus.Ok;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<CoopStatus> DepositAsync(Guid playerId, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        CoopStatus invalid = ToStatus(CoopRules.ValidateStack(itemId, quantity));
        if (invalid != CoopStatus.Ok)
        {
            return invalid;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CoopMember? me = await this.FindMemberAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (me is null)
            {
                return CoopStatus.NotInCoop;
            }

            return await this.MoveAsync(me, itemId, quantity, deposit: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<CoopStatus> WithdrawAsync(Guid playerId, int itemId, int quantity, CancellationToken cancellationToken = default)
    {
        CoopStatus invalid = ToStatus(CoopRules.ValidateStack(itemId, quantity));
        if (invalid != CoopStatus.Ok)
        {
            return invalid;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CoopMember? me = await this.FindMemberAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (me is null)
            {
                return CoopStatus.NotInCoop;
            }

            // Everything that changes the warehouse holds the gate, so what is read here is what the transfer will see.
            int inStock;
            long contributed;
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                CoopStockItem? row = await unit.Repository<CoopStockItem>().FirstOrDefaultAsync(s => s.CoopId == me.CoopId && s.ItemId == itemId, cancellationToken).ConfigureAwait(false);
                inStock = row?.Quantity ?? 0;
                List<CoopLogEntry> log = await unit.Repository<CoopLogEntry>().ListAsync(e => e.CoopId == me.CoopId && e.MemberId == me.Id && e.ItemId == itemId, cancellationToken).ConfigureAwait(false);
                contributed = log.Where(e => CoopLand.IsWarehouseReason(e.Reason)).Sum(e => (long)e.Delta);
            }

            if (quantity > inStock)
            {
                return CoopStatus.NotEnough;
            }

            if (CoopRules.CanWithdraw(me.Role == CoopRole.Founder, inStock, contributed, quantity) != CoopVerdict.Ok)
            {
                return CoopStatus.NotAllowed;
            }

            return await this.MoveAsync(me, itemId, quantity, deposit: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();

    private async Task<CoopStatus> MoveAsync(CoopMember me, int itemId, int quantity, bool deposit, CancellationToken cancellationToken)
    {
        int signed = deposit ? quantity : -quantity;
        Guid coopId = me.CoopId;
        Guid memberId = me.Id;
        TransferStatus status = await _transfers.ExecuteAsync(
            [
                new TransferLeg(TransferParty.Player(me.PlayerId), itemId, -signed),
                new TransferLeg(TransferParty.Coop(coopId), itemId, signed),
            ],
            deposit ? "coop.deposit" : "coop.withdraw",
            (unit, _) =>
            {
                unit.Repository<CoopLogEntry>().Add(new CoopLogEntry
                {
                    CoopId = coopId,
                    MemberId = memberId,
                    ItemId = itemId,
                    Delta = signed,
                    Reason = deposit ? "coop.deposit" : "coop.withdraw",
                    OccurredAt = _time.GetUtcNow(),
                });
                return Task.FromResult(TransferStatus.Ok);
            },
            cancellationToken).ConfigureAwait(false);

        if (status != TransferStatus.Ok)
        {
            return status switch
            {
                TransferStatus.NotEnoughItems => CoopStatus.NotEnough,
                TransferStatus.NoSpace => CoopStatus.NoSpace,
                _ => CoopStatus.Conflict,
            };
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            List<Guid> players = await this.MemberPlayersAsync(unit, coopId, cancellationToken).ConfigureAwait(false);
            await this.PushToAsync(players, cancellationToken).ConfigureAwait(false);
        }

        return CoopStatus.Ok;
    }

    // Tells visiting that a player is no longer in a co-op; a listener that fails must not undo the committed change.
    [SuppressMessage("Design", "CA1031", Justification = "The membership change is committed; a failing listener must not turn it into an error reply.")]
    private async Task RaiseMembershipEndedAsync(Guid playerId)
    {
        Func<Guid, ValueTask>? handlers = this.MembershipEnded;
        if (handlers is null)
        {
            return;
        }

        try
        {
            await handlers(playerId).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The visitor is re-checked at their next entry.
        }
    }

    // Items must not vanish with the land (docs/adr/ADR-024): a co-op whose chests hold items, or whose furnaces hold a job, is not dissolved, like one whose warehouse holds items.
    private async Task<bool> LandHoldsItemsAsync(IUnitOfWork unit, Guid coopId, CancellationToken cancellationToken)
    {
        List<PlacedObject> pieces = await unit.Repository<PlacedObject>().ListAsync(p => p.OwnerId == coopId, cancellationToken).ConfigureAwait(false);
        int[] furnaces = [.. pieces.Where(p => p.ItemId == ItemCatalog.Furnace).Select(p => p.Id)];
        Guid[] chests = [.. pieces.Where(p => p.ItemId == ItemCatalog.Chest).Select(p => ContainerIds.ForChest(p.Id))];
        return (chests.Length > 0 && await unit.Repository<InventorySlot>().AnyAsync(s => chests.Contains(s.PlayerId), cancellationToken).ConfigureAwait(false))
            || (furnaces.Length > 0 && await unit.Repository<FurnaceJob>().AnyAsync(j => furnaces.Contains(j.ObjectId), cancellationToken).ConfigureAwait(false));
    }

    // A dissolved co-op's land is wiped with the co-op (docs/adr/ADR-020 D6): the plots, the placed objects (their furnace jobs go by cascade) and the empty chests' inventories go in the same transaction as the co-op row.
    private async Task<List<MapInstance>> WipeLandAsync(IUnitOfWork unit, Guid coopId, CancellationToken cancellationToken)
    {
        List<MapInstance> wiped = [];
        foreach (MapSpec spec in GameMaps.All)
        {
            if (!spec.IsCoop)
            {
                continue;
            }

            IRepository<FarmPlot> plots = unit.Repository<FarmPlot>();
            foreach (FarmPlot plot in await plots.ListAsync(p => p.MapId == spec.Id && p.OwnerId == coopId, cancellationToken).ConfigureAwait(false))
            {
                plots.Remove(plot);
            }

            IRepository<PlacedObject> pieces = unit.Repository<PlacedObject>();
            foreach (PlacedObject piece in await pieces.ListAsync(p => p.MapId == spec.Id && p.OwnerId == coopId, cancellationToken).ConfigureAwait(false))
            {
                if (piece.ItemId == ItemCatalog.Chest && await unit.Repository<PlayerInventory>().FindAsync(ContainerIds.ForChest(piece.Id), cancellationToken).ConfigureAwait(false) is { } header)
                {
                    unit.Repository<PlayerInventory>().Remove(header);
                }

                pieces.Remove(piece);
            }

            wiped.Add(MapInstance.OfCoop(spec.Id, coopId));
        }

        return wiped;
    }

    private async Task<CoopMember?> FindMemberAsync(Guid playerId, CancellationToken cancellationToken)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            return await unit.Repository<CoopMember>().FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<List<Guid>> MemberPlayersAsync(IUnitOfWork unit, Guid coopId, CancellationToken cancellationToken)
    {
        List<CoopMember> members = await unit.Repository<CoopMember>().ListAsync(m => m.CoopId == coopId, cancellationToken).ConfigureAwait(false);
        return [.. members.Select(m => m.PlayerId)];
    }

    // Each online player gets their own view (their role, what they may take) as a pushed snapshot.
    private async Task PushToAsync(IEnumerable<Guid> players, CancellationToken cancellationToken)
    {
        foreach (Guid player in players.Distinct())
        {
            if (_sessions.GetActive(player) is null)
            {
                continue;
            }

            CoopView view = await this.GetAsync(player, cancellationToken).ConfigureAwait(false);
            using PacketScope<CoopSnapshot> lease = PacketFactory<CoopSnapshot>.Acquire();
            view.FillSnapshot(lease.Value);
            _ = await _notifier.TrySendAsync(player, lease.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private static CoopStatus ToStatus(CoopVerdict verdict) => verdict switch
    {
        CoopVerdict.Ok => CoopStatus.Ok,
        CoopVerdict.InvalidName => CoopStatus.InvalidName,
        CoopVerdict.UnknownItem => CoopStatus.UnknownItem,
        CoopVerdict.NotTradable => CoopStatus.NotTradable,
        CoopVerdict.InvalidQuantity => CoopStatus.InvalidQuantity,
        _ => CoopStatus.NotAllowed,
    };
}
