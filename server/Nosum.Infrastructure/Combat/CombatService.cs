// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.Data;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Look;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Abstractions.Progress;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Calendar;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Looks;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Skills;
using Nosum.Infrastructure.Progress;

namespace Nosum.Infrastructure.Combat;

/// <summary>
/// Server-authoritative implementation of <see cref="ICombatService"/> (docs/adr/ADR-045 D3).
/// </summary>
public sealed partial class CombatService : ICombatService, IDisposable
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly IGroundItems _groundItems;
    private readonly IGameDayClock _dayClock;
    private readonly IPlayerNotifier? _notifier;
    private readonly ILookService? _looks;
    private readonly IPlayerService? _players;
    private readonly IUnitOfWorkFactory? _unitOfWork;
    private readonly PlayerStateCache? _stateCache;
    private readonly ICoopMembership? _coops;
    private readonly IPlayerProgress? _progress;
    private readonly IBestiaryService? _bestiary;
    private readonly GameCalendar? _calendar;

    private readonly ConcurrentDictionary<Guid, PlayerVitalsState> _vitals = new();
    private readonly ConcurrentDictionary<string, InstanceMonsters> _instances = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(Guid PlayerId, byte SkillId), long> _skillCooldowns = new();
    private readonly ConcurrentDictionary<(Guid PlayerId, string InstanceKey, byte ShrineId), long> _shrineCooldowns = new();
    private readonly ConcurrentDictionary<(Guid PlayerId, string InstanceKey, int HazardId), long> _hazardContacts = new();
    private readonly ConcurrentDictionary<Guid, (int Count, long LastHitTick)> _playerCombos = new();

    private int RegisterHitCombo(Guid playerId)
    {
        long now = Environment.TickCount64;
        return _playerCombos.AddOrUpdate(
            playerId,
            _ => (1, now),
            (_, current) => (now - current.LastHitTick <= CombatRules.ComboTimeoutMs ? Math.Min(CombatRules.MaxComboCount, current.Count + 1) : 1, now)).Count;
    }

    /// <summary>Initializes a new instance of the <see cref="CombatService"/> class.</summary>
    public CombatService(
        IMapPresence presence,
        IMapCatalog maps,
        IInventoryService inventory,
        IGroundItems groundItems,
        IGameDayClock dayClock,
        IPlayerNotifier? notifier = null,
        ILookService? looks = null,
        IPlayerService? players = null,
        IUnitOfWorkFactory? unitOfWork = null,
        PlayerStateCache? stateCache = null,
        ICoopMembership? coops = null,
        IPlayerProgress? progress = null,
        IBestiaryService? bestiary = null,
        GameCalendar? calendar = null)
    {
        ArgumentNullException.ThrowIfNull(presence);
        ArgumentNullException.ThrowIfNull(maps);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(groundItems);
        ArgumentNullException.ThrowIfNull(dayClock);

        _presence = presence;
        _maps = maps;
        _inventory = inventory;
        _groundItems = groundItems;
        _dayClock = dayClock;
        _notifier = notifier;
        _looks = looks;
        _players = players;
        _unitOfWork = unitOfWork;
        _stateCache = stateCache;
        _coops = coops;
        _progress = progress;
        _bestiary = bestiary;
        _calendar = calendar;
        if (_progress is not null)
        {
            _progress.Combat = this;
        }

        _presence.PlayerLeft += this.OnPlayerLeftAsync;
    }

    /// <inheritdoc/>
    public async ValueTask<VitalsView> GetVitalsAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        PlayerVitalsState state = await this.GetOrCreateVitalsAsync(playerId, cancellationToken).ConfigureAwait(false);
        lock (state.Gate)
        {
            return state.ToView();
        }
    }

    /// <inheritdoc/>
    public ValueTask<MonstersView> GetMonstersAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return ValueTask.FromResult(new MonstersView(string.Empty, []));
        }

        InstanceMonsters instance = this.GetOrCreateInstance(player);
        lock (instance.Gate)
        {
            List<MonsterSpawn> alive = new(instance.Monsters.Count);
            foreach (MonsterSpawn m in instance.Monsters)
            {
                if (m.Hp > 0)
                {
                    alive.Add(m);
                }
            }

            return ValueTask.FromResult(new MonstersView(player.MapId, alive, instance.Hazards));
        }
    }

    /// <inheritdoc/>
    public async Task<AttackOutcome> AttackMonsterAsync(IConnection connection, int monsterId, int slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map))
        {
            return new AttackOutcome(CombatStatus.NotJoined, monsterId, 0, 0, 0, default, false);
        }

        PlayerVitalsState vitals = await this.GetOrCreateVitalsAsync(player.AccountId, cancellationToken).ConfigureAwait(false);
        if ((uint)slot >= (uint)InventoryLimits.DefaultCapacity)
        {
            lock (vitals.Gate)
            {
                return new AttackOutcome(CombatStatus.WrongItem, monsterId, 0, 0, 0, vitals.ToView(), false);
            }
        }

        InventorySlotView held = await _inventory.PeekSlotAsync(player.AccountId, slot, cancellationToken).ConfigureAwait(false);
        int damageBase = CombatRules.WeaponDamage(held.ItemId);
        if (held.Quantity <= 0 || damageBase <= 0)
        {
            VitalsView current;
            lock (vitals.Gate)
            {
                current = vitals.ToView();
            }

            return new AttackOutcome(CombatStatus.WrongItem, monsterId, 0, 0, 0, current, false);
        }

        int mpCost = CombatRules.AttackMpCost(held.ItemId);
        int range = CombatRules.AttackRange(held.ItemId);

        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        int tileSize = Math.Max(1, map.Grid.TileSize);
        int playerTileX = (int)Math.Floor(feetX / tileSize);
        int playerTileY = (int)Math.Floor(feetY / tileSize);

        int playerDefense = 0;
        if (_looks is not null)
        {
            PlayerLook look = await _looks.GetAsync(player.AccountId, cancellationToken).ConfigureAwait(false);
            playerDefense = CombatRules.CalculateDefense(look.HatItemId, look.ShirtItemId, look.PantsItemId);
        }

        InstanceMonsters instance = this.GetOrCreateInstance(player);
        MonsterSpawn updatedMonster;
        int damageDealt;
        int damageTaken = 0;
        int comboCount = 0;
        bool knockedOut = false;
        bool defeated = false;
        int oldLevel;
        int newLevel;
        VitalsView updatedVitals;

        lock (instance.Gate)
        {
            int idx = -1;
            for (int i = 0; i < instance.Monsters.Count; i++)
            {
                if (instance.Monsters[i].MonsterId == monsterId && instance.Monsters[i].Hp > 0)
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
            {
                lock (vitals.Gate)
                {
                    updatedVitals = vitals.ToView();
                }

                return new AttackOutcome(CombatStatus.UnknownMonster, monsterId, 0, 0, 0, updatedVitals, false);
            }

            MonsterSpawn target = instance.Monsters[idx];
            int dist = Math.Max(Math.Abs(playerTileX - target.X), Math.Abs(playerTileY - target.Y));
            if (dist > range || !LineOfSight.IsClear(map.Grid, playerTileX, playerTileY, target.X, target.Y))
            {
                lock (vitals.Gate)
                {
                    updatedVitals = vitals.ToView();
                }

                return new AttackOutcome(CombatStatus.OutOfRange, monsterId, 0, target.Hp, 0, updatedVitals, false);
            }

            lock (vitals.Gate)
            {
                if (vitals.Mp < mpCost)
                {
                    return new AttackOutcome(CombatStatus.NotEnoughMp, monsterId, 0, target.Hp, 0, vitals.ToView(), false);
                }

                vitals.Mp -= mpCost;
                oldLevel = SkillRules.LevelOf(vitals.CombatXp);
                comboCount = this.RegisterHitCombo(player.AccountId);
                damageDealt = CombatRules.CalculateDamage(held.ItemId, oldLevel, comboCount);
                if (target.Modifier == MonsterModifier.Shielded)
                {
                    damageDealt = Math.Max(1, (int)(damageDealt * 0.75f));
                }

                if (target.Modifier == MonsterModifier.FlameAura && range <= CombatRules.MeleeRange)
                {
                    vitals.Hp = Math.Max(0, vitals.Hp - 3);
                }

                int remainingHp = Math.Max(0, target.Hp - damageDealt);
                updatedMonster = target with { Hp = remainingHp };
                defeated = remainingHp == 0;

                _ = MonsterCatalog.TryGet(target.Kind, out MonsterDefinition? def);
                int baseXp = def?.XpReward ?? 15;
                if (target.Modifier != MonsterModifier.None)
                {
                    baseXp = (int)(baseXp * 2.5);
                }

                int xpGain = defeated ? baseXp : SkillRules.HitXp;
                vitals.CombatXp = Math.Min(SkillRules.XpCap, vitals.CombatXp + xpGain);
                newLevel = SkillRules.LevelOf(vitals.CombatXp);

                if (newLevel > oldLevel)
                {
                    int nextMaxHp = CombatRules.MaxHpForLevel(newLevel);
                    int nextMaxMp = CombatRules.MaxMpForLevel(newLevel);
                    int hpBonus = Math.Max(0, nextMaxHp - vitals.MaxHp);
                    int mpBonus = Math.Max(0, nextMaxMp - vitals.MaxMp);
                    vitals.MaxHp = nextMaxHp;
                    vitals.MaxMp = nextMaxMp;
                    vitals.Hp = Math.Min(nextMaxHp, vitals.Hp + hpBonus);
                    vitals.Mp = Math.Min(nextMaxMp, vitals.Mp + mpBonus);
                }

                // A monster strikes back only from its own contact reach: a staff bolt from range draws no counter-attack (issue #147).
                if (!defeated && def is not null && def.AttackDamage > 0 && CombatRules.MonsterStrikesBack(dist))
                {
                    damageTaken = CombatRules.MitigateDamage(def.AttackDamage, playerDefense);
                    vitals.Hp -= damageTaken;
                    if (damageTaken > 0 && updatedMonster.Modifier == MonsterModifier.Vampiric && updatedMonster.Hp < updatedMonster.MaxHp)
                    {
                        int vampHeal = Math.Max(1, damageTaken / 2);
                        updatedMonster = updatedMonster with { Hp = Math.Min(updatedMonster.MaxHp, updatedMonster.Hp + vampHeal) };
                    }
                }

                // A landed hit shoves an ordinary monster one tile away from the attacker; bosses stand their ground (docs/adr/ADR-076).
                if (!defeated && !CombatRules.IsBoss(updatedMonster.Kind)
                    && MonsterStepRules.TryKnockback(map.Grid, instance.Monsters, monsterId, updatedMonster.X, updatedMonster.Y, playerTileX, playerTileY, out int knockedX, out int knockedY))
                {
                    updatedMonster = updatedMonster with { X = knockedX, Y = knockedY };
                }

                instance.Monsters[idx] = updatedMonster;

                if (vitals.Hp <= 0)
                {
                    knockedOut = true;
                    vitals.Hp = CombatRules.ReviveHp(vitals.MaxHp);
                    vitals.ActiveEffect = StatusEffectKind.None;
                    vitals.EffectRemainingTicks = 0;
                    vitals.EffectMagnitude = 0;
                    vitals.ActiveBuff = StatusEffectKind.None;
                }

                updatedVitals = vitals.ToView();
            }
        }

        await this.SaveVitalsAsync(player.AccountId, updatedVitals, cancellationToken).ConfigureAwait(false);
        await this.BroadcastMonsterChangedAsync(player.Key, updatedMonster, cancellationToken).ConfigureAwait(false);

        MapInstance dropInstance = player.Instance;
        string dropKey = player.Key;

        if (knockedOut)
        {
            await this.CompleteKnockoutAsync(player, player.AccountId, updatedVitals, cancellationToken).ConfigureAwait(false);
        }

        if (defeated)
        {
            await this.HandleMonsterDefeatedAsync(knockedOut ? null : player, player.AccountId, dropKey, dropInstance, tileSize, updatedMonster, cancellationToken).ConfigureAwait(false);
        }

        // A boss kill can grant a buff to this player (docs/adr/ADR-052), so the reply carries the vitals as they are now.
        lock (vitals.Gate)
        {
            updatedVitals = vitals.ToView();
        }

        if (_notifier is not null && newLevel > oldLevel)
        {
            using PacketScope<LevelUp> levelLease = PacketFactory<LevelUp>.Acquire();
            levelLease.Value.Skill = (byte)SkillKind.Combat;
            levelLease.Value.Level = (byte)newLevel;
            _ = await _notifier.TrySendAsync(player.AccountId, levelLease.Value, cancellationToken).ConfigureAwait(false);
        }

        return new AttackOutcome(
            CombatStatus.Ok,
            monsterId,
            damageDealt,
            updatedMonster.Hp,
            damageTaken,
            updatedVitals,
            knockedOut,
            (byte)comboCount);
    }

    /// <inheritdoc/>
    public async Task<bool> CanBenefitFromPotionAsync(Guid playerId, int itemId, CancellationToken cancellationToken = default)
    {
        (int hpRestore, int mpRestore) = CombatRules.PotionRestore(itemId);
        (StatusEffectKind effect, _, _) = CombatRules.PotionEffect(itemId);
        if (effect != StatusEffectKind.None)
        {
            return true;
        }

        PlayerVitalsState vitals = await this.GetOrCreateVitalsAsync(playerId, cancellationToken).ConfigureAwait(false);
        lock (vitals.Gate)
        {
            return (hpRestore > 0 && vitals.Hp < vitals.MaxHp) || (mpRestore > 0 && vitals.Mp < vitals.MaxMp);
        }
    }

    /// <inheritdoc/>
    public async Task ApplyConsumedPotionEffectsAsync(Guid playerId, int itemId, CancellationToken cancellationToken = default)
    {
        (int hpRestore, int mpRestore) = CombatRules.PotionRestore(itemId);
        (StatusEffectKind effect, int durationSeconds, int magnitude) = CombatRules.PotionEffect(itemId);
        if (hpRestore <= 0 && mpRestore <= 0 && effect == StatusEffectKind.None)
        {
            return;
        }

        PlayerVitalsState vitals = await this.GetOrCreateVitalsAsync(playerId, cancellationToken).ConfigureAwait(false);
        VitalsView updated;
        lock (vitals.Gate)
        {
            vitals.Hp = Math.Min(vitals.MaxHp, vitals.Hp + hpRestore);
            vitals.Mp = Math.Min(vitals.MaxMp, vitals.Mp + mpRestore);
            ApplyPotionEffectLocked(vitals, effect, durationSeconds, magnitude);

            updated = vitals.ToView();
        }

        await this.SaveVitalsAsync(playerId, updated, cancellationToken).ConfigureAwait(false);
        this.SyncMoveSpeed(playerId, updated.Buff);
        if (_notifier is not null)
        {
            using PacketScope<VitalsState> lease = PacketFactory<VitalsState>.Acquire();
            lease.Value.Hp = (ushort)updated.Hp;
            lease.Value.MaxHp = (ushort)updated.MaxHp;
            lease.Value.Mp = (ushort)updated.Mp;
            lease.Value.MaxMp = (ushort)updated.MaxMp;
            lease.Value.CombatXp = updated.CombatXp;
            lease.Value.StatusEffect = updated.StatusEffect;
            lease.Value.Buff = updated.Buff;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryConsumePotionAsync(Guid playerId, int itemId, CancellationToken cancellationToken = default)
    {
        if (!await this.CanBenefitFromPotionAsync(playerId, itemId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await this.ApplyConsumedPotionEffectsAsync(playerId, itemId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public async Task<DrinkPotionOutcome> DrinkPotionAsync(IConnection connection, int slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return new DrinkPotionOutcome(CombatStatus.NotJoined, default);
        }

        PlayerVitalsState vitals = await this.GetOrCreateVitalsAsync(player.AccountId, cancellationToken).ConfigureAwait(false);
        if ((uint)slot >= (uint)InventoryLimits.DefaultCapacity)
        {
            lock (vitals.Gate)
            {
                return new DrinkPotionOutcome(CombatStatus.WrongItem, vitals.ToView());
            }
        }

        InventorySlotView held = await _inventory.PeekSlotAsync(player.AccountId, slot, cancellationToken).ConfigureAwait(false);
        (int hpRestore, int mpRestore) = CombatRules.PotionRestore(held.ItemId);
        (StatusEffectKind effect, int durationSeconds, int magnitude) = CombatRules.PotionEffect(held.ItemId);
        if (held.Quantity <= 0 || (hpRestore <= 0 && mpRestore <= 0 && effect == StatusEffectKind.None))
        {
            lock (vitals.Gate)
            {
                return new DrinkPotionOutcome(CombatStatus.WrongItem, vitals.ToView());
            }
        }

        int foodEnergy = ItemCatalog.TryGet(held.ItemId, out ItemDefinition? itemDef) && itemDef.IsEdible ? itemDef.FoodEnergy : 0;
        bool canRestoreEnergy = foodEnergy > 0
            && _progress is not null
            && await _progress.IsEnergyBelowMaxAsync(player.AccountId, cancellationToken).ConfigureAwait(false);

        lock (vitals.Gate)
        {
            bool needsHp = hpRestore > 0 && vitals.Hp < vitals.MaxHp;
            bool needsMp = mpRestore > 0 && vitals.Mp < vitals.MaxMp;
            bool hasBuff = effect != StatusEffectKind.None;
            if (!needsHp && !needsMp && !hasBuff && !canRestoreEnergy)
            {
                return new DrinkPotionOutcome(CombatStatus.AlreadyFull, vitals.ToView());
            }
        }

        InventoryChange change = await _inventory.ApplyAsync(
            player.AccountId,
            [InventoryOperation.RemoveFromSlot(slot, held.ItemId, 1)],
            "combat.potion",
            null,
            cancellationToken).ConfigureAwait(false);
        if (!change.IsOk)
        {
            lock (vitals.Gate)
            {
                return new DrinkPotionOutcome(CombatStatus.WrongItem, vitals.ToView());
            }
        }

        VitalsView updated;
        lock (vitals.Gate)
        {
            vitals.Hp = Math.Min(vitals.MaxHp, vitals.Hp + hpRestore);
            vitals.Mp = Math.Min(vitals.MaxMp, vitals.Mp + mpRestore);
            ApplyPotionEffectLocked(vitals, effect, durationSeconds, magnitude);

            updated = vitals.ToView();
        }

        await this.SaveVitalsAsync(player.AccountId, updated, cancellationToken).ConfigureAwait(false);
        this.SyncMoveSpeed(player.AccountId, updated.Buff);

        if (foodEnergy > 0 && _progress is not null)
        {
            await _progress.RestoreEnergyAsync(player.AccountId, foodEnergy, cancellationToken).ConfigureAwait(false);
        }

        if (_notifier is not null && effect != StatusEffectKind.None)
        {
            using PacketScope<VitalsState> lease = PacketFactory<VitalsState>.Acquire();
            lease.Value.Hp = (ushort)updated.Hp;
            lease.Value.MaxHp = (ushort)updated.MaxHp;
            lease.Value.Mp = (ushort)updated.Mp;
            lease.Value.MaxMp = (ushort)updated.MaxMp;
            lease.Value.CombatXp = updated.CombatXp;
            lease.Value.StatusEffect = updated.StatusEffect;
            lease.Value.Buff = updated.Buff;
            _ = await _notifier.TrySendAsync(player.AccountId, lease.Value, cancellationToken).ConfigureAwait(false);
        }

        return new DrinkPotionOutcome(CombatStatus.Ok, updated);
    }
}
