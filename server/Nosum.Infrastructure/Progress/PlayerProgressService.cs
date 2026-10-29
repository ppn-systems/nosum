// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Progress;
using Nosum.Contracts.Inventory;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.S2C;
using Nosum.Domain.Accounts;
using PlayerState = Nosum.Domain.Accounts.PlayerState;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Skills;
using Nosum.Infrastructure.Concurrency;

namespace Nosum.Infrastructure.Progress;

/// <summary>
/// <see cref="IPlayerProgress"/> on the player's <see cref="PlayerState"/> row (docs/adr/ADR-023 D4): energy, the game day it was last refilled on and the three experience
/// totals; levels are derived. The daily refill is computed lazily every time the row is read, so offline players need no tick. One lock per player serializes the
/// changes (the server is a single process, docs/adr/ADR-009); a charge keeps it until the caller disposed the charge, after the save. The lock order is always
/// inventory, then progress: a charge is only taken inside an inventory enlistment or on its own.
/// <para>
/// Docs/adr/ADR-035 D4: the row is read from <see cref="PlayerStateCache"/>, shared with the place service. A charge checks and spends against the copy under the lock,
/// stages a write of the progress columns only, and moves the copy when the caller reports the save (<see cref="IProgressCharge.PublishAsync"/>); a charge that is
/// disposed without that report drops the copy, so the next call reads the row.
/// </para>
/// </summary>
public sealed class PlayerProgressService : IPlayerProgress
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IGameDayClock _days;
    private readonly IInventoryService _inventory;
    private readonly TimeProvider _time;
    private readonly IPlayerNotifier? _notifier;
    private readonly PlayerStateCache _cache;

    /// <inheritdoc/>
    public ICombatService? Combat { get; set; }

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="days">The game day count the refill runs on.</param>
    /// <param name="inventory">Eating takes the food from it.</param>
    /// <param name="time">Clock for the row's timestamps.</param>
    /// <param name="notifier">Pushes to the player; <see langword="null"/> in tests that do not listen.</param>
    /// <param name="cache">The row copy shared with the place service; <see langword="null"/> for a private one.</param>
    public PlayerProgressService(IUnitOfWorkFactory unitOfWork, IGameDayClock days, IInventoryService inventory, TimeProvider time, IPlayerNotifier? notifier = null, PlayerStateCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(time);

        _unitOfWork = unitOfWork;
        _days = days;
        _inventory = inventory;
        _time = time;
        _notifier = notifier;
        _cache = cache ?? new PlayerStateCache();
    }

    /// <inheritdoc/>
    public async Task<PlayerProgressView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        using (await _cache.Locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            PlayerStateCache.Snapshot row = await _cache.GetAsync(_unitOfWork, playerId, cancellationToken).ConfigureAwait(false);
            return row.Exists
                ? ViewOf(row, _days.Today)
                : new PlayerProgressView(EnergyRules.MaxEnergy, EnergyRules.MaxEnergy, 0, 0, 0);
        }
    }

    /// <inheritdoc/>
    public async Task<IProgressCharge> ChargeAsync(IUnitOfWork unit, Guid playerId, ToolKind tool, SkillKind skill, int xp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);

        StripedAsyncLock.Releaser gate = await _cache.Locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false);
        try
        {
            PlayerStateCache.Snapshot row = await _cache.GetAsync(_unitOfWork, playerId, cancellationToken).ConfigureAwait(false);
            (int energy, long day) = EnergyRules.Refill(row.Energy, row.EnergyRefilledDay, _days.Today);
            int level = SkillRules.LevelOf(XpOf(row, SkillRules.SkillOf(tool)));
            int cost = EnergyRules.Cost(tool, level);
            if (!EnergyRules.TrySpend(energy, cost, out int left))
            {
                return new Charge(this, gate, playerId, allowed: false, 0, 0, 0, 0, SkillKind.None, 0, 0, null);
            }

            int before = XpOf(row, skill);
            int after = skill == SkillKind.None ? before : Math.Min(SkillRules.XpCap, before + Math.Max(0, xp));
            PlayerStateCache.Snapshot next = SetXp(row with { Exists = true, Energy = left, EnergyRefilledDay = day, UpdatedAt = _time.GetUtcNow() }, skill, after);
            this.Stage(unit, playerId, row.Exists, next);
            return new Charge(this, gate, playerId, allowed: true, left, next.FarmingXp, next.ForagingXp, next.MiningXp, skill, SkillRules.LevelOf(before), SkillRules.LevelOf(after), next);
        }
        catch
        {
            gate.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    [SuppressMessage("Design", "CA1031", Justification = "A failed save is reported as a refused use; the caller asks again.")]
    public async Task<ChargeStatus> TryChargeAsync(Guid playerId, ToolKind tool, SkillKind skill, int xp, CancellationToken cancellationToken = default)
    {
        IUnitOfWork unit = _unitOfWork.Create();
        await using (unit.ConfigureAwait(false))
        {
            using IProgressCharge charge = await this.ChargeAsync(unit, playerId, tool, skill, xp, cancellationToken).ConfigureAwait(false);
            if (!charge.Allowed)
            {
                return ChargeStatus.NoEnergy;
            }

            try
            {
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (unit.IsUniqueViolation(ex) || unit.IsConcurrencyConflict(ex))
            {
                return ChargeStatus.Conflict;
            }

            await charge.PublishAsync(cancellationToken).ConfigureAwait(false);
            return ChargeStatus.Ok;
        }
    }

    /// <inheritdoc/>
    public async Task<EatOutcome> EatAsync(Guid playerId, int hotbarSlot, CancellationToken cancellationToken = default)
    {
        if ((uint)hotbarSlot >= (uint)InventoryLimits.HotbarSlots)
        {
            return new EatOutcome(EatStatus.InvalidSlot);
        }

        InventorySlotView held = await _inventory.PeekSlotAsync(playerId, hotbarSlot, cancellationToken).ConfigureAwait(false);
        if (held.ItemId == 0 || !ItemCatalog.TryGet(held.ItemId, out ItemDefinition? food) || !food.IsEdible)
        {
            return new EatOutcome(EatStatus.NotEdible);
        }

        EatStatus refused = EatStatus.Conflict;
        StripedAsyncLock.Releaser gate = default;
        bool locked = false;
        bool staged = false;
        PlayerStateCache.Snapshot? saved = null;
        int energyAfter = 0;
        bool isPotion = Combat is not null && CombatRules.IsPotion(held.ItemId);
        bool canRestoreVitals = isPotion && await Combat!.CanBenefitFromPotionAsync(playerId, held.ItemId, cancellationToken).ConfigureAwait(false);
        try
        {
            InventoryChange change = await _inventory.ApplyAsync(
                playerId,
                [InventoryOperation.RemoveFromSlot(hotbarSlot, held.ItemId, 1)],
                "eat",
                async (unit, ct) =>
                {
                    gate = await _cache.Locks.EnterAsync(playerId, ct).ConfigureAwait(false);
                    locked = true;
                    PlayerStateCache.Snapshot row = await _cache.GetAsync(_unitOfWork, playerId, ct).ConfigureAwait(false);
                    (int energy, long day) = EnergyRules.Refill(row.Energy, row.EnergyRefilledDay, _days.Today);
                    if (energy >= EnergyRules.MaxEnergy && !canRestoreVitals)
                    {
                        refused = EatStatus.EnergyFull;
                        return false;
                    }

                    energyAfter = EnergyRules.Eat(energy, food.FoodEnergy);
                    saved = row with { Exists = true, Energy = energyAfter, EnergyRefilledDay = day, UpdatedAt = _time.GetUtcNow() };
                    this.Stage(unit, playerId, row.Exists, saved);
                    staged = true;
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                if (staged)
                {
                    // The save did not go through (a lost race): whether the row changed is unknown, so the copy is read again.
                    _cache.Forget(playerId);
                }

                return new EatOutcome(change.Status switch
                {
                    InventoryStatus.Conflict => refused,
                    _ => EatStatus.ItemMissing,
                });
            }

            // Saved with the food: the copy follows, still under the progress lock.
            PlayerStateCache.Snapshot eaten = saved!;
            _cache.Apply(playerId, s => s with { Exists = true, Energy = eaten.Energy, EnergyRefilledDay = eaten.EnergyRefilledDay, UpdatedAt = eaten.UpdatedAt });
        }
        finally
        {
            if (locked)
            {
                gate.Dispose();
            }
        }

        if (isPotion)
        {
            await Combat!.ApplyConsumedPotionEffectsAsync(playerId, held.ItemId, cancellationToken).ConfigureAwait(false);
        }

        await this.PushEnergyAsync(playerId, energyAfter, cancellationToken).ConfigureAwait(false);
        return new EatOutcome(EatStatus.Ok);
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsEnergyBelowMaxAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        PlayerProgressView view = await this.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        return view.Energy < EnergyRules.MaxEnergy;
    }

    /// <inheritdoc/>
    public async ValueTask RestoreEnergyFromPotionAsync(Guid playerId, int foodEnergy, CancellationToken cancellationToken = default)
    {
        if (foodEnergy <= 0)
        {
            return;
        }

        int energyAfter;
        using (await _cache.Locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            PlayerStateCache.Snapshot row = await _cache.GetAsync(_unitOfWork, playerId, cancellationToken).ConfigureAwait(false);
            (int energy, long day) = EnergyRules.Refill(row.Energy, row.EnergyRefilledDay, _days.Today);
            if (energy >= EnergyRules.MaxEnergy)
            {
                return;
            }

            energyAfter = EnergyRules.Eat(energy, foodEnergy);
            PlayerStateCache.Snapshot next = row with { Exists = true, Energy = energyAfter, EnergyRefilledDay = day, UpdatedAt = _time.GetUtcNow() };
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                this.Stage(unit, playerId, row.Exists, next);
                _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            _cache.Apply(playerId, s => s with { Exists = true, Energy = next.Energy, EnergyRefilledDay = next.EnergyRefilledDay, UpdatedAt = next.UpdatedAt });
        }

        await this.PushEnergyAsync(playerId, energyAfter, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task PushAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        PlayerProgressView view = await this.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        await this.PushEnergyAsync(playerId, view.Energy, cancellationToken).ConfigureAwait(false);
        await this.PushXpAsync(playerId, view.FarmingXp, view.ForagingXp, view.MiningXp, cancellationToken).ConfigureAwait(false);
    }

    private static PlayerProgressView ViewOf(PlayerStateCache.Snapshot row, long today)
    {
        (int energy, _) = EnergyRules.Refill(row.Energy, row.EnergyRefilledDay, today);
        return new PlayerProgressView(energy, EnergyRules.MaxEnergy, row.FarmingXp, row.ForagingXp, row.MiningXp);
    }

    private static int XpOf(PlayerStateCache.Snapshot row, SkillKind skill) => skill switch
    {
        SkillKind.Farming => row.FarmingXp,
        SkillKind.Foraging => row.ForagingXp,
        SkillKind.Mining => row.MiningXp,
        _ => 0,
    };

    private static PlayerStateCache.Snapshot SetXp(PlayerStateCache.Snapshot row, SkillKind skill, int xp) => skill switch
    {
        SkillKind.Farming => row with { FarmingXp = xp },
        SkillKind.Foraging => row with { ForagingXp = xp },
        SkillKind.Mining => row with { MiningXp = xp },
        _ => row,
    };

    // Stages the write of the progress columns in the caller's unit; a player without a row gets one, with no saved place (an empty map name, which the player service reads as "none").
    private void Stage(IUnitOfWork unit, Guid playerId, bool exists, PlayerStateCache.Snapshot next)
    {
        PlayerState row = new()
        {
            PlayerId = playerId,
            MapName = exists ? next.MapName : string.Empty,
            Energy = next.Energy,
            EnergyRefilledDay = next.EnergyRefilledDay,
            FarmingXp = next.FarmingXp,
            ForagingXp = next.ForagingXp,
            MiningXp = next.MiningXp,
            UpdatedAt = next.UpdatedAt,
        };
        if (exists)
        {
            // Only the progress columns: the place service owns the others and may be saving them right now.
            unit.Repository<PlayerState>().Update(
                row,
                nameof(PlayerState.Energy),
                nameof(PlayerState.EnergyRefilledDay),
                nameof(PlayerState.FarmingXp),
                nameof(PlayerState.ForagingXp),
                nameof(PlayerState.MiningXp),
                nameof(PlayerState.UpdatedAt));
        }
        else
        {
            unit.Repository<PlayerState>().Add(row);
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "The change is saved; a failed push only delays the display until the next one.")]
    private async ValueTask PushEnergyAsync(Guid playerId, int energy, CancellationToken cancellationToken)
    {
        if (_notifier is null)
        {
            return;
        }

        try
        {
            using PacketScope<EnergyChanged> lease = PacketFactory<EnergyChanged>.Acquire();
            lease.Value.Energy = (ushort)energy;
            lease.Value.MaxEnergy = EnergyRules.MaxEnergy;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort, see above.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "The change is saved; a failed push only delays the display until the next one.")]
    private async ValueTask PushXpAsync(Guid playerId, int farming, int foraging, int mining, CancellationToken cancellationToken)
    {
        if (_notifier is null)
        {
            return;
        }

        try
        {
            using PacketScope<SkillXpChanged> lease = PacketFactory<SkillXpChanged>.Acquire();
            lease.Value.FarmingXp = farming;
            lease.Value.ForagingXp = foraging;
            lease.Value.MiningXp = mining;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort, see above.
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "The change is saved; a failed push only delays the display until the next one.")]
    private async ValueTask PushLevelUpAsync(Guid playerId, SkillKind skill, int level, CancellationToken cancellationToken)
    {
        if (_notifier is null)
        {
            return;
        }

        try
        {
            using PacketScope<LevelUp> lease = PacketFactory<LevelUp>.Acquire();
            lease.Value.Skill = (byte)skill;
            lease.Value.Level = (byte)level;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Best effort, see above.
        }
    }

    private sealed class Charge : IProgressCharge
    {
        private readonly PlayerProgressService _owner;
        private readonly StripedAsyncLock.Releaser _gate;
        private readonly Guid _playerId;
        private readonly int _energy;
        private readonly int _farming;
        private readonly int _foraging;
        private readonly int _mining;
        private readonly SkillKind _skill;
        private readonly int _levelBefore;
        private readonly int _levelAfter;
        private readonly PlayerStateCache.Snapshot? _next;
        private int _released;
        private int _published;

        public Charge(PlayerProgressService owner, StripedAsyncLock.Releaser gate, Guid playerId, bool allowed, int energy, int farming, int foraging, int mining, SkillKind skill, int levelBefore, int levelAfter, PlayerStateCache.Snapshot? next)
        {
            _owner = owner;
            _gate = gate;
            _playerId = playerId;
            this.Allowed = allowed;
            _energy = energy;
            _farming = farming;
            _foraging = foraging;
            _mining = mining;
            _skill = skill;
            _levelBefore = levelBefore;
            _levelAfter = levelAfter;
            _next = next;
        }

        public bool Allowed { get; }

        public async ValueTask PublishAsync(CancellationToken cancellationToken = default)
        {
            if (!this.Allowed)
            {
                return;
            }

            // The caller saved: the copy follows the row before anything is pushed.
            if (Interlocked.Exchange(ref _published, 1) == 0)
            {
                PlayerStateCache.Snapshot next = _next!;
                _owner._cache.Apply(_playerId, s => s with { Exists = true, Energy = next.Energy, EnergyRefilledDay = next.EnergyRefilledDay, FarmingXp = next.FarmingXp, ForagingXp = next.ForagingXp, MiningXp = next.MiningXp, UpdatedAt = next.UpdatedAt });
            }

            await _owner.PushEnergyAsync(_playerId, _energy, cancellationToken).ConfigureAwait(false);
            if (_skill != SkillKind.None)
            {
                await _owner.PushXpAsync(_playerId, _farming, _foraging, _mining, cancellationToken).ConfigureAwait(false);
                for (int level = _levelBefore + 1; level <= _levelAfter; level++)
                {
                    await _owner.PushLevelUpAsync(_playerId, _skill, level, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                if (this.Allowed && Volatile.Read(ref _published) == 0)
                {
                    // Staged but never reported as saved (the save failed, or the caller gave up): the row may or may not have changed.
                    _owner._cache.Forget(_playerId);
                }

                _gate.Dispose();
            }
        }
    }
}
