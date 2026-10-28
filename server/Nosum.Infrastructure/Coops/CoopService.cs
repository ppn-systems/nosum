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
using Nosum.Abstractions.Chat;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Economy;
using Nosum.Abstractions.Farm;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Mail;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Coop;
using Nosum.Contracts.Coop.S2C;
using Nosum.Domain.Buildings;
using Nosum.Domain.Coops;
using Nosum.Domain.Farm;
using Nosum.Domain.Inventory;
using Nosum.Domain.Nodes;
using Nosum.Domain.Placeables;
using Nosum.Gameplay.Coops;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Mail;
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
    private readonly IInventoryService _inventory;
    private readonly CoopDirectory _directory;
    private readonly IMapPresence _presence;
    private readonly ISessionRegistry _sessions;
    private readonly IPlayerNotifier _notifier;
    private readonly INicknameDirectory _nicknames;
    private readonly IFarmService _farm;
    private readonly ICoopLandEviction _eviction;
    private readonly IMailService? _mail;
    private readonly IBlockList? _blocks;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="transfers">Moves items between inventories and the warehouse.</param>
    /// <param name="inventory">The owner of the inventory tables: the deletion of a wiped chest's inventory and the check whether chests hold items go through it.</param>
    /// <param name="presence">Who is on which map (an invitation goes to a player on the inviter's map).</param>
    /// <param name="sessions">Who is connected.</param>
    /// <param name="notifier">Pushes snapshots to online members.</param>
    /// <param name="nicknames">Resolves member nicknames.</param>
    /// <param name="farm">Told to forget the land of a dissolved co-op.</param>
    /// <param name="eviction">Takes a member off co-op land when they leave or are removed (docs/adr/ADR-020).</param>
    /// <param name="time">The clock; tests pass a fake one.</param>
    /// <param name="mail">Receives a mail for an invited player (docs/adr/ADR-029 D5); <see langword="null"/> to send none.</param>
    /// <param name="blocks">The block lists: an invitation between players where either blocked the other is dropped (docs/adr/ADR-029 D3); <see langword="null"/> to check none.</param>
    /// <param name="directory">Who is in which co-op, held in memory (docs/adr/ADR-035 D5); this service drops what it changed. <see langword="null"/> reads the rows every time.</param>
    public CoopService(IUnitOfWorkFactory unitOfWork, ITransferService transfers, IInventoryService inventory, IMapPresence presence, ISessionRegistry sessions, IPlayerNotifier notifier, INicknameDirectory nicknames, IFarmService farm, ICoopLandEviction eviction, TimeProvider time, IMailService? mail = null, IBlockList? blocks = null, CoopDirectory? directory = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(transfers);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(nicknames);
        ArgumentNullException.ThrowIfNull(farm);
        ArgumentNullException.ThrowIfNull(eviction);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _transfers = transfers;
        _inventory = inventory;
        _presence = presence;
        _sessions = sessions;
        _notifier = notifier;
        _nicknames = nicknames;
        _farm = farm;
        _eviction = eviction;
        _time = time;
        _mail = mail;
        _blocks = blocks;
        _directory = directory ?? new CoopDirectory(unitOfWork, cached: false);
    }

    /// <inheritdoc/>
    public event Func<Guid, ValueTask>? MembershipEnded;

    /// <inheritdoc/>
    public async Task<CoopView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
        => (await this.BuildViewsAsync([playerId], cancellationToken).ConfigureAwait(false))[playerId];

    // The views of several players at once: what the players of one co-op share (the stock, the recent log) is read once for all of them, and so are their contributions.
    private async Task<Dictionary<Guid, CoopView>> BuildViewsAsync(IReadOnlyList<Guid> players, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        Dictionary<Guid, CoopView> views = [];
        Dictionary<Guid, List<(Guid Player, CoopRecord Coop, CoopMemberRecord Me)>> byCoop = [];
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            foreach (Guid playerId in players)
            {
                CoopRecord? mine = await _directory.OfPlayerAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (mine?.MemberOf(playerId) is not { } me)
                {
                    views[playerId] = await this.InvitationsViewAsync(unit, playerId, now, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!byCoop.TryGetValue(mine.Id, out List<(Guid Player, CoopRecord Coop, CoopMemberRecord Me)>? group))
                {
                    group = [];
                    byCoop[mine.Id] = group;
                }

                group.Add((playerId, mine, me));
            }

            foreach ((Guid coopId, List<(Guid Player, CoopRecord Coop, CoopMemberRecord Me)> group) in byCoop)
            {
                CoopRecord coop = group[0].Coop;
                HashSet<Guid> memberIds = [.. coop.Members.Select(m => m.Id)];
                IReadOnlyDictionary<Guid, string> names = await _nicknames.GetManyAsync(coop.Members.Select(m => m.PlayerId), cancellationToken).ConfigureAwait(false);
                Guid[] asking = [.. group.Select(g => g.Me.Id)];
                List<CoopLogEntry> logs = await unit.Repository<CoopLogEntry>().ListAsync(e => e.CoopId == coopId && asking.Contains(e.MemberId), cancellationToken).ConfigureAwait(false);
                List<CoopStockItem> stock = await unit.Repository<CoopStockItem>().ListAsync(s => s.CoopId == coopId, cancellationToken).ConfigureAwait(false);
                List<CoopLogEntry> recent = await unit.Repository<CoopLogEntry>().PageAsync(e => e.CoopId == coopId && (e.Reason == "coop.deposit" || e.Reason == "coop.withdraw"), e => e.OccurredAt, 0, CoopRules.RecentLogLines, cancellationToken).ConfigureAwait(false);
                List<CoopLogView> logViews = [.. recent.Select(e => new CoopLogView(memberIds.Contains(e.MemberId) ? e.MemberId : Guid.Empty, e.ItemId, e.Delta, e.OccurredAt))];

                foreach ((Guid playerId, CoopRecord mine, CoopMemberRecord me) in group)
                {
                    List<CoopMemberView> memberViews = [.. mine.Members
                        .Select(m => new CoopMemberView(m.Id, m.Role, m.Id == me.Id, _sessions.GetActive(m.PlayerId) is not null, names.GetValueOrDefault(m.PlayerId, string.Empty)))];
                    Dictionary<int, long> contributed = logs.Where(e => e.MemberId == me.Id && CoopLand.IsWarehouseReason(e.Reason)).GroupBy(e => e.ItemId).ToDictionary(g => g.Key, g => g.Sum(e => (long)e.Delta));
                    List<CoopStockView> stockViews = [.. stock
                        .OrderBy(s => s.ItemId)
                        .Select(s => new CoopStockView(s.ItemId, s.Quantity, CoopRules.Withdrawable(me.Role == CoopRole.Founder, s.Quantity, contributed.GetValueOrDefault(s.ItemId))))];
                    views[playerId] = new CoopView(true, mine.Name, me.Role, memberViews, stockViews, logViews, [], now, mine.WarehouseLevel, mine.LandLevel);
                }
            }
        }

        return views;
    }

    // What a player outside any co-op sees: the invitations that wait for them.
    private async Task<CoopView> InvitationsViewAsync(IUnitOfWork unit, Guid playerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<CoopInvite> invites = await unit.Repository<CoopInvite>().ListAsync(i => i.PlayerId == playerId && i.ExpiresAt > now, cancellationToken).ConfigureAwait(false);
        List<CoopInviteView> views = [];
        foreach (CoopInvite invite in invites)
        {
            Coop? offered = await unit.Repository<Coop>().FindAsync(invite.CoopId, cancellationToken).ConfigureAwait(false);
            if (offered is not null)
            {
                CoopMember? founder = await unit.Repository<CoopMember>().FirstOrDefaultAsync(m => m.CoopId == offered.Id && m.Role == CoopRole.Founder, cancellationToken).ConfigureAwait(false);
                string inviter = founder is null ? string.Empty : await _nicknames.GetAsync(founder.PlayerId, cancellationToken).ConfigureAwait(false);
                views.Add(new CoopInviteView(invite.Id, offered.Name, inviter));
            }
        }

        return new CoopView(false, string.Empty, CoopRole.None, [], [], [], views, now);
    }

    /// <inheritdoc/>
    public async Task<CoopStatus> CreateAsync(Guid playerId, string name, CancellationToken cancellationToken = default)
    {
        if (name is null || CoopRules.ValidateName(name) != CoopVerdict.Ok)
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
                finally
                {
                    _directory.Changed(coop.Id, playerId);
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

    // The invitation is stored and pushed; the mail is the copy that waits for a player who is offline (docs/adr/ADR-029 D5).
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "The invitation is committed; a failing mail must not turn it into an error for the inviter.")]
    private async Task MailInviteAsync(string coopName, Guid inviterId, Guid inviteeId, CancellationToken cancellationToken)
    {
        if (_mail is null)
        {
            return;
        }

        try
        {
            string inviter = await _nicknames.GetAsync(inviterId, cancellationToken).ConfigureAwait(false);
            _ = await _mail.SendAsync(inviteeId, MailRules.Codes.CoopInvite, [coopName, inviter], null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // See above.
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
                CoopRecord? mine = await _directory.OfPlayerAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (mine?.MemberOf(playerId) is not { } member)
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
                if (_blocks is not null && await _blocks.IsBlockedEitherWayAsync(playerId, invitee, cancellationToken).ConfigureAwait(false))
                {
                    return CoopStatus.TargetNotFound;
                }

                if (await _directory.OfPlayerAsync(invitee, cancellationToken).ConfigureAwait(false) is not null)
                {
                    return CoopStatus.TargetInCoop;
                }

                IRepository<CoopInvite> invites = unit.Repository<CoopInvite>();
                CoopInvite? existing = await invites.FirstOrDefaultAsync(i => i.CoopId == mine.Id && i.PlayerId == invitee, cancellationToken).ConfigureAwait(false);
                if (existing is not null)
                {
                    if (existing.ExpiresAt > now)
                    {
                        return CoopStatus.AlreadyInvited;
                    }

                    invites.Remove(existing);
                }

                invites.Add(new CoopInvite { CoopId = mine.Id, PlayerId = invitee, CreatedAt = now, ExpiresAt = now.AddHours(CoopRules.InviteHours) });
                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (unit.IsUniqueViolation(ex))
                {
                    return CoopStatus.AlreadyInvited;
                }

                await this.PushToAsync([invitee], cancellationToken).ConfigureAwait(false);
                await this.MailInviteAsync(mine.Name, playerId, invitee, cancellationToken).ConfigureAwait(false);
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
                    if (await _directory.OfPlayerAsync(playerId, cancellationToken).ConfigureAwait(false) is not null)
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
                    finally
                    {
                        _directory.Changed(invite.CoopId, playerId);
                    }

                    toPush.AddRange(await this.MemberPlayersAsync(playerId, cancellationToken).ConfigureAwait(false));
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
                        await _eviction.EvictAsync(playerId, cancellationToken).ConfigureAwait(false);
                        Coop? coop = await unit.Repository<Coop>().FindAsync(me.CoopId, cancellationToken).ConfigureAwait(false);
                        if (coop is not null)
                        {
                            unit.Repository<Coop>().Remove(coop);
                            dissolvedLand = await this.WipeLandAsync(unit, coop.Id, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }

                memberRepository.Remove(me);
                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _directory.Changed(me.CoopId, playerId);
                }

                toPush.AddRange(others.Select(m => m.PlayerId));
            }

            foreach (MapInstance land in dissolvedLand)
            {
                _farm.Forget(land);
            }

            if (dissolvedLand.Count == 0)
            {
                await _eviction.EvictAsync(playerId, cancellationToken).ConfigureAwait(false);
            }

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
                try
                {
                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _directory.Changed(me.CoopId, target.PlayerId);
                }

                removedPlayer = target.PlayerId;
                toPush.Add(target.PlayerId);
                toPush.AddRange(await this.MemberPlayersAsync(playerId, cancellationToken).ConfigureAwait(false));
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
            if (await this.FindMemberAsync(playerId, cancellationToken).ConfigureAwait(false) is not { } me)
            {
                return CoopStatus.NotInCoop;
            }

            // The warehouse holds so many item kinds (docs/adr/ADR-038); the gate keeps the count from changing under this check.
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                CoopRecord? coop = await _directory.OfPlayerAsync(playerId, cancellationToken).ConfigureAwait(false);
                List<CoopStockItem> held = await unit.Repository<CoopStockItem>().ListAsync(s => s.CoopId == me.CoopId, cancellationToken).ConfigureAwait(false);
                if (!CoopUpgrades.CanStore(coop?.WarehouseLevel ?? 0, held.Count, held.Exists(s => s.ItemId == itemId)))
                {
                    return CoopStatus.WarehouseFull;
                }
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
            if (await this.FindMemberAsync(playerId, cancellationToken).ConfigureAwait(false) is not { } me)
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
    public async Task<CoopStatus> UpgradeAsync(Guid playerId, CoopUpgradeTrack track, int targetLevel, CancellationToken cancellationToken = default)
    {
        CoopTrack rulesTrack = track switch
        {
            CoopUpgradeTrack.Warehouse => CoopTrack.Warehouse,
            CoopUpgradeTrack.Land => CoopTrack.Land,
            _ => (CoopTrack)0,
        };

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await this.FindMemberAsync(playerId, cancellationToken).ConfigureAwait(false) is not { } me)
            {
                return CoopStatus.NotInCoop;
            }

            if (me.Role != CoopRole.Founder)
            {
                return CoopStatus.NotFounder;
            }

            // The gate serializes every change of the warehouse and of the levels, so what is read here is what the transfer sees; the enlisted work still re-checks the level.
            Guid coopId = me.CoopId;
            Guid memberId = me.Id;
            int current;
            Dictionary<int, int> stock;
            IUnitOfWork read = _unitOfWork.Create();
            await using (read.ConfigureAwait(false))
            {
                CoopRecord? coop = await _directory.OfPlayerAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (coop is null)
                {
                    return CoopStatus.NotInCoop;
                }

                current = rulesTrack == CoopTrack.Land ? coop.LandLevel : coop.WarehouseLevel;
                stock = (await read.Repository<CoopStockItem>().ListAsync(s => s.CoopId == coopId, cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.ItemId, s => s.Quantity);
            }

            UpgradeVerdict verdict = CoopUpgrades.CanBuy(rulesTrack, current, targetLevel, item => stock.GetValueOrDefault(item));
            if (verdict != UpgradeVerdict.Ok)
            {
                return verdict switch
                {
                    UpgradeVerdict.MaxLevel => CoopStatus.MaxLevel,
                    UpgradeVerdict.NotEnoughStock => CoopStatus.NotEnough,
                    _ => CoopStatus.AlreadyUpgraded,
                };
            }

            string reason = CoopUpgrades.ReasonOf(rulesTrack);
            List<TransferLeg> legs = [.. CoopUpgrades.Cost(rulesTrack, targetLevel).Select(c => new TransferLeg(TransferParty.Coop(coopId), c.ItemId, -c.Quantity))];

            // A transfer needs a player party to run in; the buyer takes part with a leg that moves nothing, and the price goes to nobody.
            List<TransferLeg> transfer = [new TransferLeg(TransferParty.Player(playerId), 0, 0), .. legs];
            bool stale = false;
            TransferStatus status = await _transfers.ExecuteAsync(
                transfer,
                reason,
                async (unit, token) =>
                {
                    Coop? coop = await unit.Repository<Coop>().FindAsync(coopId, token).ConfigureAwait(false);
                    int level = coop is null ? -1 : rulesTrack == CoopTrack.Land ? coop.LandLevel : coop.WarehouseLevel;
                    if (coop is null || level != targetLevel - 1)
                    {
                        stale = true;
                        return TransferStatus.Refused;
                    }

                    if (rulesTrack == CoopTrack.Land)
                    {
                        coop.LandLevel = targetLevel;
                    }
                    else
                    {
                        coop.WarehouseLevel = targetLevel;
                    }

                    DateTimeOffset now = _time.GetUtcNow();
                    foreach (TransferLeg leg in legs)
                    {
                        unit.Repository<CoopLogEntry>().Add(new CoopLogEntry { CoopId = coopId, MemberId = memberId, ItemId = leg.ItemId, Delta = leg.Items, Reason = reason, OccurredAt = now });
                    }

                    return TransferStatus.Ok;
                },
                cancellationToken).ConfigureAwait(false);

            // The level may have changed in the row whatever the status says (a lost race): the next read comes from the rows.
            _directory.Changed(coopId);
            if (status != TransferStatus.Ok)
            {
                return stale ? CoopStatus.AlreadyUpgraded : status == TransferStatus.NotEnoughItems ? CoopStatus.NotEnough : CoopStatus.Conflict;
            }

            await this.PushToAsync(await this.MemberPlayersAsync(playerId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            return CoopStatus.Ok;
        }
        finally
        {
            _ = _gate.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _gate.Dispose();

    private async Task<CoopStatus> MoveAsync(Me me, int itemId, int quantity, bool deposit, CancellationToken cancellationToken)
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

        await this.PushToAsync(await this.MemberPlayersAsync(me.PlayerId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
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

    // Items must not vanish with the land (docs/adr/ADR-024): a co-op whose chests hold items, or whose furnaces hold a job, or whose buildings hold animals or rations, is not dissolved, like one whose warehouse holds items.
    private async Task<bool> LandHoldsItemsAsync(IUnitOfWork unit, Guid coopId, CancellationToken cancellationToken)
    {
        List<PlacedObject> pieces = await unit.Repository<PlacedObject>().ListAsync(p => p.OwnerId == coopId, cancellationToken).ConfigureAwait(false);
        int[] furnaces = [.. pieces.Where(p => p.ItemId == ItemCatalog.Furnace).Select(p => p.Id)];
        Guid[] chests = [.. pieces.Where(p => p.ItemId == ItemCatalog.Chest).Select(p => ContainerIds.ForChest(p.Id))];
        if ((chests.Length > 0 && await _inventory.AnyContainerHoldsItemsAsync(unit, chests, cancellationToken).ConfigureAwait(false))
            || (furnaces.Length > 0 && await unit.Repository<FurnaceJob>().AnyAsync(j => furnaces.Contains(j.ObjectId), cancellationToken).ConfigureAwait(false)))
        {
            return true;
        }

        List<FarmBuilding> buildings = await unit.Repository<FarmBuilding>().ListAsync(b => b.OwnerId == coopId, cancellationToken).ConfigureAwait(false);
        if (buildings.Exists(b => b.Rations > 0))
        {
            return true;
        }

        Guid[] buildingIds = [.. buildings.Select(b => b.Id)];
        return buildingIds.Length > 0 && await unit.Repository<FarmAnimal>().AnyAsync(a => buildingIds.Contains(a.BuildingId), cancellationToken).ConfigureAwait(false);
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
            List<PlacedObject> placed = await pieces.ListAsync(p => p.MapId == spec.Id && p.OwnerId == coopId, cancellationToken).ConfigureAwait(false);
            int[] chestIds = [.. placed.Where(p => p.ItemId == ItemCatalog.Chest).Select(p => p.Id)];
            if (chestIds.Length > 0)
            {
                IRepository<ChestLogEntry> chestLogs = unit.Repository<ChestLogEntry>();
                foreach (ChestLogEntry log in await chestLogs.ListAsync(l => chestIds.Contains(l.ObjectId), cancellationToken).ConfigureAwait(false))
                {
                    chestLogs.Remove(log);
                }
            }

            foreach (PlacedObject piece in placed)
            {
                if (piece.ItemId == ItemCatalog.Chest)
                {
                    _ = await _inventory.DropContainerAsync(unit, ContainerIds.ForChest(piece.Id), onlyIfEmpty: false, cancellationToken).ConfigureAwait(false);
                }

                pieces.Remove(piece);
            }

            IRepository<WorldNode> nodes = unit.Repository<WorldNode>();
            foreach (WorldNode node in await nodes.ListAsync(n => n.MapId == spec.Id && n.OwnerId == coopId, cancellationToken).ConfigureAwait(false))
            {
                nodes.Remove(node);
            }

            IRepository<NodeInstanceState> nodeStates = unit.Repository<NodeInstanceState>();
            foreach (NodeInstanceState state in await nodeStates.ListAsync(s => s.MapId == spec.Id && s.OwnerId == coopId, cancellationToken).ConfigureAwait(false))
            {
                nodeStates.Remove(state);
            }

            wiped.Add(MapInstance.OfCoop(spec.Id, coopId));
        }

        return wiped;
    }

    private async Task<Me?> FindMemberAsync(Guid playerId, CancellationToken cancellationToken)
    {
        CoopRecord? coop = await _directory.OfPlayerAsync(playerId, cancellationToken).ConfigureAwait(false);
        return coop?.MemberOf(playerId) is { } member ? new Me(member.Id, coop.Id, playerId, member.Role) : null;
    }

    // The players of the co-op the given member belongs to, from the directory (after a change it was dropped, so this reads the rows again once).
    private async Task<List<Guid>> MemberPlayersAsync(Guid memberPlayerId, CancellationToken cancellationToken)
    {
        CoopRecord? coop = await _directory.OfPlayerAsync(memberPlayerId, cancellationToken).ConfigureAwait(false);
        return coop is null ? [] : [.. coop.Members.Select(m => m.PlayerId)];
    }

    private readonly record struct Me(Guid Id, Guid CoopId, Guid PlayerId, CoopRole Role);

    // Each online player gets their own view (their role, what they may take) as a pushed snapshot.
    private async Task PushToAsync(IEnumerable<Guid> players, CancellationToken cancellationToken)
    {
        List<Guid> online = [.. players.Distinct().Where(p => _sessions.GetActive(p) is not null)];
        if (online.Count == 0)
        {
            return;
        }

        Dictionary<Guid, CoopView> views = await this.BuildViewsAsync(online, cancellationToken).ConfigureAwait(false);
        foreach (Guid player in online)
        {
            using PacketScope<CoopSnapshot> lease = PacketFactory<CoopSnapshot>.Acquire();
            views[player].FillSnapshot(lease.Value);
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
