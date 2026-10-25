// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Look;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Look;
using Nosum.Contracts.Look.S2C;
using Nosum.Domain.Looks;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Looks;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Looks;

/// <summary>
/// <see cref="ILookService"/>: stores each player's look, validates every change (avatar parts against <see cref="LookCatalog"/>,
/// clothing against the inventory), moves worn items out of and back into the inventory in the same transaction as the look row,
/// and tells the map with ids only. Looks are cached in memory after the first read (single process, docs/adr/ADR-009).
/// </summary>
public sealed class LookService : ILookService
{
    // What a new player finds in the inventory so the wardrobe has something to try. A product default (docs/adr/ADR-014).
    private static readonly int[] s_starterWardrobe =
    [
        ItemCatalog.ShirtRed,
        ItemCatalog.ShirtGreen,
        ItemCatalog.PantsBlue,
        ItemCatalog.HatStraw,
        ItemCatalog.HatCap,
    ];

    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IInventoryService _inventory;
    private readonly IMapPresence _presence;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<Guid, PlayerLook> _cache = new();
    private readonly StripedAsyncLock _locks = new();

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="inventory">Moves clothing between the inventory and the body.</param>
    /// <param name="presence">Who is on which map; looks are announced through it.</param>
    /// <param name="time">The clock for the saved timestamp; tests pass a fake one.</param>
    public LookService(IUnitOfWorkFactory unitOfWork, IInventoryService inventory, IMapPresence presence, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _inventory = inventory;
        _presence = presence;
        _time = time;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PlayerLookView>> AnnounceAndGetAsync(IConnection connection, Guid playerId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? me))
        {
            return [];
        }

        PlayerLook mine = await this.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        await this.BroadcastAsync(me, mine, connection, cancellationToken).ConfigureAwait(false);

        List<PlayerPresence> players = [];
        _presence.CopyPlayers(me.MapId, players);
        List<PlayerLookView> looks = new(players.Count);
        foreach (PlayerPresence other in players)
        {
            PlayerLook look = other.AccountId == playerId ? mine : await this.GetAsync(other.AccountId, cancellationToken).ConfigureAwait(false);
            looks.Add(new PlayerLookView(other.PlayerId, look));
        }

        return looks;
    }

    /// <inheritdoc/>
    public async Task<PlayerLook> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(playerId, out PlayerLook cached))
        {
            return cached;
        }

        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            return await this.LoadLockedAsync(playerId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<LookStatus> SetAppearanceAsync(IConnection connection, Guid playerId, int skinId, int hairStyleId, int hairDyeId, CancellationToken cancellationToken = default)
    {
        LookVerdict verdict = LookCatalog.ValidateAppearance(skinId, hairStyleId, hairDyeId);
        if (verdict != LookVerdict.Ok)
        {
            return verdict switch
            {
                LookVerdict.InvalidSkin => LookStatus.InvalidSkin,
                LookVerdict.InvalidHairStyle => LookStatus.InvalidHairStyle,
                _ => LookStatus.InvalidHairDye,
            };
        }

        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            PlayerLook current = await this.LoadLockedAsync(playerId, cancellationToken).ConfigureAwait(false);
            PlayerLook next = current with { SkinId = skinId, HairStyleId = hairStyleId, HairDyeId = hairDyeId };
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                PlayerAppearance? row = await unit.Repository<PlayerAppearance>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                if (row is null)
                {
                    return LookStatus.Conflict;
                }

                Apply(row, next, _time.GetUtcNow());
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _cache[playerId] = next;
            await this.BroadcastAsync(connection, next, cancellationToken).ConfigureAwait(false);
            return LookStatus.Ok;
        }
    }

    /// <inheritdoc/>
    public async Task<LookStatus> EquipAsync(IConnection connection, Guid playerId, int slot, int itemId, CancellationToken cancellationToken = default)
    {
        if (!ItemCatalog.TryGet(itemId, out ItemDefinition? item) || item.Kind != ItemKind.Clothing || item.Wear == WearSlot.None)
        {
            return LookStatus.NotWearable;
        }

        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            PlayerLook current = await this.LoadLockedAsync(playerId, cancellationToken).ConfigureAwait(false);
            int before = current.Worn(item.Wear);

            // The item leaves the slot it is in; what was worn takes its place in the inventory. One transaction with the look row.
            List<InventoryOperation> operations = [InventoryOperation.RemoveFromSlot(slot, itemId, 1)];
            if (before != 0)
            {
                operations.Add(InventoryOperation.Add(before, 1));
            }

            PlayerLook next = current.With(item.Wear, itemId);
            return await this.CommitAsync(connection, playerId, operations, next, "wardrobe.equip", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<LookStatus> UnequipAsync(IConnection connection, Guid playerId, int wearSlot, CancellationToken cancellationToken = default)
    {
        WearSlot where = (WearSlot)wearSlot;
        if (where is not (WearSlot.Shirt or WearSlot.Pants or WearSlot.Hat))
        {
            return LookStatus.InvalidSlot;
        }

        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            PlayerLook current = await this.LoadLockedAsync(playerId, cancellationToken).ConfigureAwait(false);
            int worn = current.Worn(where);
            if (worn == 0)
            {
                return LookStatus.NothingWorn;
            }

            return await this.CommitAsync(connection, playerId, [InventoryOperation.Add(worn, 1)], current.With(where, 0), "wardrobe.unequip", cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Apply(PlayerAppearance row, PlayerLook look, DateTimeOffset now)
    {
        row.SkinId = look.SkinId;
        row.HairStyleId = look.HairStyleId;
        row.HairDyeId = look.HairDyeId;
        row.ShirtItemId = look.ShirtItemId;
        row.PantsItemId = look.PantsItemId;
        row.HatItemId = look.HatItemId;
        row.UpdatedAt = now;
    }

    private async Task<LookStatus> CommitAsync(IConnection connection, Guid playerId, List<InventoryOperation> operations, PlayerLook next, string reason, CancellationToken cancellationToken)
    {
        InventoryChange change = await _inventory.ApplyAsync(
            playerId,
            operations,
            reason,
            async (unit, token) =>
            {
                PlayerAppearance? row = await unit.Repository<PlayerAppearance>().FindAsync(playerId, token).ConfigureAwait(false);
                if (row is null)
                {
                    return false;
                }

                Apply(row, next, _time.GetUtcNow());
                return true;
            },
            cancellationToken).ConfigureAwait(false);

        if (!change.IsOk)
        {
            return change.Status switch
            {
                InventoryStatus.NoSpace => LookStatus.NoSpace,
                InventoryStatus.Conflict => LookStatus.Conflict,
                _ => LookStatus.InvalidSlot,
            };
        }

        _cache[playerId] = next;
        await this.BroadcastAsync(connection, next, cancellationToken).ConfigureAwait(false);
        return LookStatus.Ok;
    }

    // Loads the look (creating the default one and the starter wardrobe for a new player). Call while holding the player's look lock.
    private async Task<PlayerLook> LoadLockedAsync(Guid playerId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(playerId, out PlayerLook cached))
        {
            return cached;
        }

        bool created = false;
        PlayerLook look;
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            PlayerAppearance? row = await unit.Repository<PlayerAppearance>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                look = PlayerLook.Default;
                row = new PlayerAppearance { PlayerId = playerId };
                Apply(row, look, _time.GetUtcNow());
                unit.Repository<PlayerAppearance>().Add(row);
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                created = true;
            }
            else
            {
                look = new PlayerLook(row.SkinId, row.HairStyleId, row.HairDyeId, row.ShirtItemId, row.PantsItemId, row.HatItemId);
            }
        }

        if (created)
        {
            // A full inventory just means a new player starts without the extra clothes; it is not an error.
            List<InventoryOperation> gifts = [];
            foreach (int itemId in s_starterWardrobe)
            {
                gifts.Add(InventoryOperation.Add(itemId, 1));
            }

            _ = await _inventory.ApplyAsync(playerId, gifts, "starter.wardrobe", null, cancellationToken).ConfigureAwait(false);
        }

        _cache[playerId] = look;
        return look;
    }

    private async Task BroadcastAsync(IConnection connection, PlayerLook look, CancellationToken cancellationToken)
    {
        if (_presence.TryGet(connection, out PlayerPresence? me))
        {
            await this.BroadcastAsync(me, look, null, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task BroadcastAsync(PlayerPresence me, PlayerLook look, IConnection? except, CancellationToken cancellationToken)
    {
        using PacketScope<LookChanged> lease = PacketFactory<LookChanged>.Acquire();
        // A pooled packet may come back with its nested row reset: always give it a fresh one.
        LookRow row = LookRow.Create();
        new PlayerLookView(me.PlayerId, look).FillRow(row);
        lease.Value.Look = row;
        await _presence.MulticastAsync(me.MapId, lease.Value, except, cancellationToken).ConfigureAwait(false);
    }
}
