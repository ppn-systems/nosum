// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Nalix.Codec.Pooling;
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
/// </summary>
public sealed class PlayerProgressService : IPlayerProgress
{
    private readonly IUnitOfWorkFactory _unitOfWork;
    private readonly IGameDayClock _days;
    private readonly IInventoryService _inventory;
    private readonly TimeProvider _time;
    private readonly IPlayerNotifier? _notifier;
    private readonly StripedAsyncLock _locks = new(64);

    /// <summary>Initializes the service.</summary>
    /// <param name="unitOfWork">Creates units of work.</param>
    /// <param name="days">The game day count the refill runs on.</param>
    /// <param name="inventory">Eating takes the food from it.</param>
    /// <param name="time">Clock for the row's timestamps.</param>
    /// <param name="notifier">Pushes to the player; <see langword="null"/> in tests that do not listen.</param>
    public PlayerProgressService(IUnitOfWorkFactory unitOfWork, IGameDayClock days, IInventoryService inventory, TimeProvider time, IPlayerNotifier? notifier = null)
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
    }

    /// <inheritdoc/>
    public async Task<PlayerProgressView> GetAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        using (await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false))
        {
            IUnitOfWork unit = _unitOfWork.Create();
            await using (unit.ConfigureAwait(false))
            {
                PlayerState? row = await unit.Repository<PlayerState>().FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                return row is null
                    ? new PlayerProgressView(EnergyRules.MaxEnergy, EnergyRules.MaxEnergy, 0, 0, 0)
                    : ViewOf(row, _days.Today);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IProgressCharge> ChargeAsync(IUnitOfWork unit, Guid playerId, ToolKind tool, SkillKind skill, int xp, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);

        StripedAsyncLock.Releaser gate = await _locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false);
        try
        {
            PlayerState row = await this.RowAsync(unit, playerId, cancellationToken).ConfigureAwait(false);
            (int energy, long day) = EnergyRules.Refill(row.Energy, row.EnergyRefilledDay, _days.Today);
            int level = SkillRules.LevelOf(XpOf(row, SkillRules.SkillOf(tool)));
            int cost = EnergyRules.Cost(tool, level);
            if (!EnergyRules.TrySpend(energy, cost, out int left))
            {
                return new Charge(this, gate, playerId, allowed: false, 0, 0, 0, 0, SkillKind.None, 0, 0);
            }

            row.Energy = left;
            row.EnergyRefilledDay = day;
            int before = XpOf(row, skill);
            int after = skill == SkillKind.None ? before : Math.Min(SkillRules.XpCap, before + Math.Max(0, xp));
            SetXp(row, skill, after);
            row.UpdatedAt = _time.GetUtcNow();
            return new Charge(this, gate, playerId, allowed: true, left, row.FarmingXp, row.ForagingXp, row.MiningXp, skill, SkillRules.LevelOf(before), SkillRules.LevelOf(after));
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
        int energyAfter = 0;
        try
        {
            InventoryChange change = await _inventory.ApplyAsync(
                playerId,
                [InventoryOperation.RemoveFromSlot(hotbarSlot, held.ItemId, 1)],
                "eat",
                async (unit, ct) =>
                {
                    gate = await _locks.EnterAsync(playerId, ct).ConfigureAwait(false);
                    locked = true;
                    PlayerState row = await this.RowAsync(unit, playerId, ct).ConfigureAwait(false);
                    (int energy, long day) = EnergyRules.Refill(row.Energy, row.EnergyRefilledDay, _days.Today);
                    if (energy >= EnergyRules.MaxEnergy)
                    {
                        refused = EatStatus.EnergyFull;
                        return false;
                    }

                    row.Energy = EnergyRules.Eat(energy, food.FoodEnergy);
                    row.EnergyRefilledDay = day;
                    row.UpdatedAt = _time.GetUtcNow();
                    energyAfter = row.Energy;
                    return true;
                },
                cancellationToken).ConfigureAwait(false);
            if (!change.IsOk)
            {
                return new EatOutcome(change.Status switch
                {
                    InventoryStatus.Conflict => refused,
                    _ => EatStatus.ItemMissing,
                });
            }
        }
        finally
        {
            if (locked)
            {
                gate.Dispose();
            }
        }

        await this.PushEnergyAsync(playerId, energyAfter, cancellationToken).ConfigureAwait(false);
        return new EatOutcome(EatStatus.Ok);
    }

    /// <inheritdoc/>
    public async Task PushAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        PlayerProgressView view = await this.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        await this.PushEnergyAsync(playerId, view.Energy, cancellationToken).ConfigureAwait(false);
        await this.PushXpAsync(playerId, view.FarmingXp, view.ForagingXp, view.MiningXp, cancellationToken).ConfigureAwait(false);
    }

    private static PlayerProgressView ViewOf(PlayerState row, long today)
    {
        (int energy, _) = EnergyRules.Refill(row.Energy, row.EnergyRefilledDay, today);
        return new PlayerProgressView(energy, EnergyRules.MaxEnergy, row.FarmingXp, row.ForagingXp, row.MiningXp);
    }

    private static int XpOf(PlayerState row, SkillKind skill) => skill switch
    {
        SkillKind.Farming => row.FarmingXp,
        SkillKind.Foraging => row.ForagingXp,
        SkillKind.Mining => row.MiningXp,
        _ => 0,
    };

    private static void SetXp(PlayerState row, SkillKind skill, int xp)
    {
        switch (skill)
        {
            case SkillKind.Farming:
                row.FarmingXp = xp;
                break;
            case SkillKind.Foraging:
                row.ForagingXp = xp;
                break;
            case SkillKind.Mining:
                row.MiningXp = xp;
                break;
        }
    }

    // The player's state row, made when the player has none yet. Such a row has no place (empty map name), which the player service reads as "no saved place".
    private async Task<PlayerState> RowAsync(IUnitOfWork unit, Guid playerId, CancellationToken cancellationToken)
    {
        IRepository<PlayerState> states = unit.Repository<PlayerState>();
        PlayerState? row = await states.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new PlayerState { PlayerId = playerId, MapName = string.Empty, UpdatedAt = _time.GetUtcNow() };
            states.Add(row);
        }

        return row;
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
        private int _released;

        public Charge(PlayerProgressService owner, StripedAsyncLock.Releaser gate, Guid playerId, bool allowed, int energy, int farming, int foraging, int mining, SkillKind skill, int levelBefore, int levelAfter)
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
        }

        public bool Allowed { get; }

        public async ValueTask PublishAsync(CancellationToken cancellationToken = default)
        {
            if (!this.Allowed)
            {
                return;
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
                _gate.Dispose();
            }
        }
    }
}
