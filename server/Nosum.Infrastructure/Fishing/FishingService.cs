// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nosum.Abstractions.Fishing;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Progress;
using Nosum.Contracts.Fishing;
using Nosum.Contracts.Inventory;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Fishing;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Skills;
using Nosum.Gameplay.Weather;

namespace Nosum.Infrastructure.Fishing;

/// <summary>
/// <see cref="IFishingService"/> (docs/adr/ADR-078). Every rule is in <c>Nosum.Gameplay.Fishing</c>; this class only reads the state the rules need
/// (the held item, the water of the map, the reach, the room, the clock and the calendar) and commits the result in one unit of work. The energy is
/// charged when the cast opens and the catch goes into the inventory with its experience when the line is reeled in; a refusal changes nothing.
/// </summary>
public sealed class FishingService : IFishingService
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly IPlayerProgress _progress;
    private readonly GameCalendar _calendar;
    private readonly TimeProvider _time;
    private readonly FishingCasts _casts = new();

    /// <summary>Initializes the service.</summary>
    /// <param name="presence">Finds the player's place and position.</param>
    /// <param name="maps">The map definitions, for the water grid and the walk grid size.</param>
    /// <param name="inventory">Holds the rod, takes the room check and the catch.</param>
    /// <param name="progress">Charges the energy and the fishing experience.</param>
    /// <param name="calendar">The season, the hour and the weather of a roll.</param>
    /// <param name="time">The clock that times the cast and the press; <see langword="null"/> for the system clock.</param>
    public FishingService(IMapPresence presence, IMapCatalog maps, IInventoryService inventory, IPlayerProgress progress, GameCalendar calendar, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(calendar);

        _presence = presence;
        _maps = maps;
        _inventory = inventory;
        _progress = progress;
        _calendar = calendar;
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<CastOutcome> CastAsync(IConnection connection, int tileX, int tileY, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        _casts.Sweep(nowMs);

        // A player with no place on a map cannot reach any water.
        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return new CastOutcome(FishingStatus.NotWater);
        }

        Guid playerId = player.AccountId;
        if (!ItemCatalog.TryGet(_inventory.PeekHeldItemId(playerId), out ItemDefinition? held) || held.Tool != ToolKind.FishingRod)
        {
            return new CastOutcome(FishingStatus.NoRod);
        }

        if (FishingWaters.Of(player.MapId) == FishingWater.None
            || !_maps.TryGet(player.MapId, out MapDefinition? map)
            || map.Water is not { } water
            || (uint)tileX >= (uint)map.Grid.Width
            || (uint)tileY >= (uint)map.Grid.Height
            || !water.IsWater(tileX, tileY))
        {
            return new CastOutcome(FishingStatus.NotWater);
        }

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        if (!FishingRules.InCastReach(feetX, feetY, tileX, tileY, map.Grid.TileSize))
        {
            return new CastOutcome(FishingStatus.TooFar);
        }

        // Advisory: the catch itself is added in the reel-in's transaction, which refuses a full inventory too.
        if (!HasRoomForCatch(await _inventory.GetAsync(playerId, cancellationToken).ConfigureAwait(false)))
        {
            return new CastOutcome(FishingStatus.NoSpace);
        }

        PlayerProgressView progress = await _progress.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        uint castId = _casts.NextId();
        ulong seed = FishingRules.SeedOf(PlayerKeyOf(playerId), castId, nowMs, tileX, tileY);
        int biteDelayMs = FishingRules.BiteDelayMs(seed);
        int windowMs = FishingRules.WindowMs(SkillRules.LevelOf(progress.FishingXp));

        FishingStatus refusal = FishingStatus.Conflict;
        IProgressCharge? charge = null;
        try
        {
            InventoryChange change = await _inventory.ApplyAsync(
                playerId,
                [],
                "fishing.cast",
                async (unit, token) =>
                {
                    charge = await _progress.ChargeEnergyAsync(unit, playerId, FishingRules.CastEnergy, SkillKind.Fishing, 0, token).ConfigureAwait(false);
                    if (!charge.Allowed)
                    {
                        refusal = FishingStatus.OutOfEnergy;
                        return false;
                    }

                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                return new CastOutcome(refusal);
            }

            // Saved with the energy: the copy follows, then the cast opens. The pushes are best effort.
            await charge!.PublishAsync(cancellationToken).ConfigureAwait(false);
            _casts.Open(new OpenCast(castId, playerId, player.Key, player.MapId, nowMs, biteDelayMs, windowMs, seed));
            return new CastOutcome(FishingStatus.Ok, castId, biteDelayMs, windowMs);
        }
        finally
        {
            charge?.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task<ReelOutcome> ReelInAsync(IConnection connection, uint castId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        long nowMs = _time.GetUtcNow().ToUnixTimeMilliseconds();
        if (castId == 0 || !_presence.TryGet(connection, out PlayerPresence? player))
        {
            return new ReelOutcome(FishingStatus.NoCast);
        }

        Guid playerId = player.AccountId;
        if (!_casts.TryTake(playerId, castId, out OpenCast? cast))
        {
            return new ReelOutcome(FishingStatus.NoCast);
        }

        // The cast is dropped (not kept) when the player changed place or let it wait past its lifetime.
        if (cast.InstanceKey != player.Key || nowMs - cast.CastAtMs > FishingRules.CastLifetimeMs)
        {
            return new ReelOutcome(FishingStatus.NoCast);
        }

        HookVerdict verdict = FishingRules.Judge(nowMs - cast.CastAtMs, cast.BiteDelayMs, cast.WindowMs);
        if (verdict == HookVerdict.TooEarly)
        {
            _casts.Restore(cast);
            return new ReelOutcome(FishingStatus.TooEarly);
        }

        if (verdict == HookVerdict.Escaped)
        {
            return new ReelOutcome(FishingStatus.Escaped);
        }

        PlayerProgressView progress = await _progress.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        FishingSpot spot = new(
            FishingWaters.Of(cast.MapId),
            _calendar.SeasonAt(nowMs),
            WeatherRules.At(_calendar, nowMs),
            _calendar.At(nowMs).Hour,
            SkillRules.LevelOf(progress.FishingXp));
        FishSpecies? species = FishingRules.Roll(spot, cast.Seed);
        int xp = FishingRules.CatchXp(species);
        int sizeCm = species is null ? 0 : FishingRules.SizeCm(species.Rarity, cast.Seed);

        IReadOnlyList<InventoryOperation> operations = species is null ? [] : [InventoryOperation.Add(species.ItemId, 1)];
        IProgressCharge? charge = null;
        try
        {
            InventoryChange change = await _inventory.ApplyAsync(
                playerId,
                operations,
                "fishing.catch",
                async (unit, token) =>
                {
                    charge = await _progress.ChargeEnergyAsync(unit, playerId, 0, SkillKind.Fishing, xp, token).ConfigureAwait(false);
                    return charge.Allowed;
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                // Nothing changed: the fish is still on the line, so the cast stays open for a second press once there is room.
                _casts.Restore(cast);
                return new ReelOutcome(change.Status == InventoryStatus.NoSpace ? FishingStatus.NoSpace : FishingStatus.Conflict);
            }

            await charge!.PublishAsync(cancellationToken).ConfigureAwait(false);
            return species is null
                ? new ReelOutcome(FishingStatus.Nothing, 0, 0, xp, 0)
                : new ReelOutcome(FishingStatus.Ok, species.ItemId, 1, xp, sizeCm);
        }
        finally
        {
            charge?.Dispose();
        }
    }

    // The first 64 bits of the account id: a stable key of the player for the seed (docs/adr/ADR-078 D5).
    private static ulong PlayerKeyOf(Guid playerId)
        => BinaryPrimitives.ReadUInt64LittleEndian(playerId.ToByteArray());

    // A catch needs room for at least one of the species: the inventory's own rule, on the stacks it holds now.
    private static bool HasRoomForCatch(InventoryView view)
    {
        InventoryGrid grid = new(view.Capacity);
        foreach (InventorySlotView slot in view.Slots)
        {
            grid.Load(slot.Slot, new ItemSlot(slot.ItemId, slot.Quantity));
        }

        foreach (FishSpecies species in FishCatalog.All)
        {
            if (grid.AvailableRoomFor(species.ItemId) > 0)
            {
                return true;
            }
        }

        return false;
    }
}
