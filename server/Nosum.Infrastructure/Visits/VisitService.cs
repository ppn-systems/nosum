// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Accounts;
using Nosum.Abstractions.Buildings;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Friends;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Visits;
using Nosum.Contracts.Visit;
using Nosum.Domain.Accounts;
using Nosum.Domain.Visits;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Players;
using Nosum.Gameplay.Visits;

namespace Nosum.Infrastructure.Visits;

/// <summary>
/// <see cref="IVisitService"/>: the allow-list and the co-op setting live in PostgreSQL; a visit is checked by <see cref="VisitAccess"/> every time and
/// carried out by <see cref="IPlayerService.TravelAsync"/>. Whenever a permission can disappear (a player is removed from the list, the co-op setting
/// is turned off, a co-op member leaves or is removed) every visitor who is in a place they may no longer enter is sent to the Town before the call returns
/// (docs/adr/ADR-019 D5).
/// </summary>
public sealed class VisitService : IVisitService, IDisposable
{
    private static readonly TimeSpan s_retryDelay = TimeSpan.FromMilliseconds(100);
    private const int SweepAttempts = 4;

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IMapPresence _presence;
    private readonly IPlayerService _players;
    private readonly IInstanceAccessPolicy _access;
    private readonly INicknameDirectory _nicknames;
    private readonly ICoopService _coops;
    private readonly IFriendService? _friends;
    private readonly TimeProvider _time;
    private readonly IBuildingDirectory? _buildings;

    /// <summary>Initializes the service and starts listening for co-op members who leave.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="presence">Who stands where.</param>
    /// <param name="players">Moves players between places.</param>
    /// <param name="access">The rule for entering a private place (the same one the world uses).</param>
    /// <param name="nicknames">Resolves nicknames for the list.</param>
    /// <param name="coops">Tells when a member's co-op membership ends.</param>
    /// <param name="time">The clock for the audit timestamps; tests pass a fake one.</param>
    /// <param name="buildings">The farm buildings of the owner's land: a visitor inside one is in the owner's place too (docs/adr/ADR-028 D2); <see langword="null"/> for none.</param>
    /// <param name="friends">Tells when visiting rights through friendship end (docs/adr/ADR-029 D4); <see langword="null"/> to ignore friends.</param>
    public VisitService(IUnitOfWorkFactory unitOfWork, IMapPresence presence, IPlayerService players, IInstanceAccessPolicy access, INicknameDirectory nicknames, ICoopService coops, TimeProvider time, IFriendService? friends = null, IBuildingDirectory? buildings = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(nicknames);
        ArgumentNullException.ThrowIfNull(coops);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _presence = presence;
        _players = players;
        _access = access;
        _nicknames = nicknames;
        _coops = coops;
        _time = time;
        _buildings = buildings;
        _friends = friends;
        _coops.MembershipEnded += this.OnMembershipEndedAsync;
        if (_friends is not null)
        {
            _friends.FriendVisitsRevoked += this.OnFriendVisitsRevokedAsync;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _coops.MembershipEnded -= this.OnMembershipEndedAsync;
        if (_friends is not null)
        {
            _friends.FriendVisitsRevoked -= this.OnFriendVisitsRevokedAsync;
        }
    }

    /// <inheritdoc/>
    public async Task<VisitorsView> GetAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        List<Guid> listed;
        bool coopMembersMayVisit;
        bool isGuest;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? owner = await unit.Repository<Player>().FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
            isGuest = owner?.IsGuest ?? true;
            VisitSettings? settings = await unit.Repository<VisitSettings>().FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
            coopMembersMayVisit = settings?.CoopMembersMayVisit ?? VisitRules.CoopMembersMayVisitByDefault;
            listed = [.. (await unit.Repository<VisitGrant>().ListAsync(g => g.OwnerId == ownerId, cancellationToken).ConfigureAwait(false)).OrderBy(g => g.CreatedAt).Select(g => g.VisitorId)];
        }

        HashSet<Guid> present = [.. this.PlayersInPlacesOf(ownerId).Select(p => p.AccountId)];
        List<Guid> everyone = [.. listed, .. present.Where(id => !listed.Contains(id))];
        IReadOnlyDictionary<Guid, string> names = await _nicknames.GetManyAsync(everyone, cancellationToken).ConfigureAwait(false);

        List<VisitorView> visitors = new(everyone.Count);
        foreach (Guid id in everyone)
        {
            visitors.Add(new VisitorView(id, names.TryGetValue(id, out string? name) ? name : string.Empty, listed.Contains(id), present.Contains(id)));
        }

        return new VisitorsView(coopMembersMayVisit, isGuest, visitors);
    }

    /// <inheritdoc/>
    public async Task<VisitStatus> AddAsync(Guid ownerId, string nickname, CancellationToken cancellationToken = default)
    {
        if (!TryKey(nickname, out string key))
        {
            return VisitStatus.InvalidNickname;
        }

        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? owner = await unit.Repository<Player>().FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
            if (owner is null || owner.IsGuest)
            {
                return VisitStatus.GuestOwner;
            }

            Player? target = await unit.Repository<Player>().FirstOrDefaultAsync(p => p.NormalizedNickname == key, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                return VisitStatus.UnknownPlayer;
            }

            if (target.Id == ownerId)
            {
                return VisitStatus.Self;
            }

            IRepository<VisitGrant> grants = unit.Repository<VisitGrant>();
            if (await grants.AnyAsync(g => g.OwnerId == ownerId && g.VisitorId == target.Id, cancellationToken).ConfigureAwait(false))
            {
                return VisitStatus.AlreadyListed;
            }

            if (await grants.CountAsync(g => g.OwnerId == ownerId, cancellationToken).ConfigureAwait(false) >= VisitRules.MaxListed)
            {
                return VisitStatus.ListFull;
            }

            grants.Add(new VisitGrant { OwnerId = ownerId, VisitorId = target.Id, CreatedAt = _time.GetUtcNow() });
            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (unit.IsUniqueViolation(ex))
            {
                return VisitStatus.AlreadyListed;
            }

            return VisitStatus.Ok;
        }
    }

    /// <inheritdoc/>
    public async Task<VisitStatus> RemoveAsync(Guid ownerId, Guid visitorId, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            IRepository<VisitGrant> grants = unit.Repository<VisitGrant>();
            VisitGrant? grant = await grants.FirstOrDefaultAsync(g => g.OwnerId == ownerId && g.VisitorId == visitorId, cancellationToken).ConfigureAwait(false);
            if (grant is null)
            {
                return VisitStatus.NotListed;
            }

            grants.Remove(grant);
            _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        // A co-op member removed from the list may still be let in through the co-op setting; the sweep asks the policy, so only those who may not stay are sent out.
        await this.SweepAsync(ownerId, visitorId).ConfigureAwait(false);
        return VisitStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<VisitStatus> KickAsync(Guid ownerId, Guid visitorId, CancellationToken cancellationToken = default)
    {
        if (visitorId == ownerId)
        {
            return VisitStatus.Self;
        }

        PlayerPresence? present = this.PlayersInPlacesOf(ownerId).FirstOrDefault(p => p.AccountId == visitorId);
        if (present is null)
        {
            return VisitStatus.NotListed;
        }

        TravelStatus status = await _players.TravelAsync(present.Connection, MapInstance.Shared(GameMaps.Town), cancellationToken).ConfigureAwait(false);
        return status == TravelStatus.Arrived ? VisitStatus.Ok : VisitStatus.Conflict;
    }

    /// <inheritdoc/>
    public async Task<VisitStatus> SetCoopMembersMayVisitAsync(Guid ownerId, bool allowed, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? owner = await unit.Repository<Player>().FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
            if (owner is null || owner.IsGuest)
            {
                return VisitStatus.GuestOwner;
            }

            VisitSettings? settings = await unit.Repository<VisitSettings>().FindAsync(ownerId, cancellationToken).ConfigureAwait(false);
            if (settings is null)
            {
                settings = new VisitSettings { OwnerId = ownerId };
                unit.Repository<VisitSettings>().Add(settings);
            }

            settings.CoopMembersMayVisit = allowed;
            settings.UpdatedAt = _time.GetUtcNow();
            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
            {
                return VisitStatus.Conflict;
            }
        }

        if (!allowed)
        {
            await this.SweepAsync(ownerId, null).ConfigureAwait(false);
        }

        return VisitStatus.Ok;
    }

    /// <inheritdoc/>
    public async Task<VisitStatus> VisitAsync(IConnection connection, Guid visitorId, string ownerNickname, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!TryKey(ownerNickname, out string key))
        {
            return VisitStatus.InvalidNickname;
        }

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return VisitStatus.NotJoined;
        }

        // A visit starts on a public map: from a private place a player walks out first (docs/adr/ADR-019 D4).
        if (player.Instance.IsPrivate)
        {
            return VisitStatus.NotOnPublicMap;
        }

        Guid ownerId;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            Player? owner = await unit.Repository<Player>().FirstOrDefaultAsync(p => p.NormalizedNickname == key, cancellationToken).ConfigureAwait(false);
            if (owner is null)
            {
                return VisitStatus.UnknownPlayer;
            }

            ownerId = owner.Id;
        }

        if (ownerId == visitorId)
        {
            return VisitStatus.Self;
        }

        MapInstance destination = new(GameMaps.Farm, ownerId);
        if (!await _access.CanEnterAsync(destination, visitorId, cancellationToken).ConfigureAwait(false))
        {
            return VisitStatus.NotAllowed;
        }

        return await _players.TravelAsync(connection, destination, cancellationToken).ConfigureAwait(false) switch
        {
            TravelStatus.Arrived => VisitStatus.Ok,
            TravelStatus.NotJoined => VisitStatus.NotJoined,
            _ => VisitStatus.NotAllowed,
        };
    }

    private static bool TryKey(string? typed, out string key)
    {
        string name = NicknameRules.Normalize(typed);
        if (NicknameRules.Validate(name) == NicknameVerdict.Ok || NicknameRules.IsGuestName(name, out _))
        {
            key = NicknameRules.Key(name);
            return true;
        }

        key = string.Empty;
        return false;
    }

    // Every player in the owner's farm and farmhouse, and inside the buildings on their farm, other than the owner.
    private List<PlayerPresence> PlayersInPlacesOf(Guid ownerId)
    {
        List<PlayerPresence> found = [];
        foreach (MapSpec map in GameMaps.All)
        {
            if (map.IsPrivate && !map.IsInterior)
            {
                _presence.CopyPlayers(new MapInstance(map.Id, ownerId).Key, found);
            }
        }

        if (_buildings is not null)
        {
            foreach (MapInstance interior in _buildings.InteriorsOfPlayer(ownerId))
            {
                _presence.CopyPlayers(interior.Key, found);
            }
        }

        _ = found.RemoveAll(p => p.AccountId == ownerId);
        return found;
    }

    // Sends to the Town every visitor in the owner's places (or just one of them) who may no longer be there. A visitor in the middle of a
    // map change cannot be moved at that instant, so the sweep looks again a moment later.
    private async Task SweepAsync(Guid ownerId, Guid? only)
    {
        for (int attempt = 0; attempt < SweepAttempts; attempt++)
        {
            bool blocked = false;
            foreach (PlayerPresence visitor in this.PlayersInPlacesOf(ownerId))
            {
                if ((only is { } one && visitor.AccountId != one) || await _access.CanEnterAsync(visitor.Instance, visitor.AccountId, CancellationToken.None).ConfigureAwait(false))
                {
                    continue;
                }

                TravelStatus status = await _players.TravelAsync(visitor.Connection, MapInstance.Shared(GameMaps.Town), CancellationToken.None).ConfigureAwait(false);
                blocked |= status != TravelStatus.Arrived;
            }

            if (!blocked)
            {
                return;
            }

            await Task.Delay(s_retryDelay, _time, CancellationToken.None).ConfigureAwait(false);
        }
    }

    // An owner turned "friends may visit" off, or a friendship ended: those who may no longer be there are sent out.
    [SuppressMessage("Design", "CA1031", Justification = "The friends change is committed; a failing sweep must not undo it. The visitor is re-checked at their next entry.")]
    private async ValueTask OnFriendVisitsRevokedAsync(Guid ownerId, Guid? visitorId)
    {
        try
        {
            await this.SweepAsync(ownerId, visitorId).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // See above.
        }
    }

    // A player left a co-op: they may no longer be let in through it, and those who came to them through it may no longer be allowed to stay.
    [SuppressMessage("Design", "CA1031", Justification = "The co-op change is committed; a failing sweep must not undo it. The visitor is re-checked at their next entry.")]
    private async ValueTask OnMembershipEndedAsync(Guid playerId)
    {
        try
        {
            // As an owner: the co-op members who were visiting them.
            await this.SweepAsync(playerId, null).ConfigureAwait(false);

            // As a visitor: the owners whose places they are in.
            List<string> keys = [];
            _presence.CopyActiveMapIds(keys);
            HashSet<Guid> owners = [];
            foreach (string key in keys.Where(k => k.Contains('@', StringComparison.Ordinal)))
            {
                List<PlayerPresence> here = [];
                _presence.CopyPlayers(key, here);
                foreach (PlayerPresence p in here.Where(p => p.AccountId == playerId && p.Instance.IsPrivate && p.Instance.OwnerId != playerId))
                {
                    // Inside a building the place is the land's owner's (a co-op's land is not a player's place: those members leave through the co-op eviction).
                    Guid owner = p.Instance.OwnerId;
                    if (GameMaps.TryGet(p.MapId, out MapSpec? spec) && spec.IsInterior)
                    {
                        BuildingInfo? building = _buildings is null ? null : await _buildings.FindAsync(owner, CancellationToken.None).ConfigureAwait(false);
                        owner = building is { Land.IsCoopLand: false } ? building.Land.OwnerId : Guid.Empty;
                    }

                    if (owner != Guid.Empty && owner != playerId)
                    {
                        _ = owners.Add(owner);
                    }
                }
            }

            foreach (Guid ownerId in owners)
            {
                await this.SweepAsync(ownerId, playerId).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // See above.
        }
    }
}
