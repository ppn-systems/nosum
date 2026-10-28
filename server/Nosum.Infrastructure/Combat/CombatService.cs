// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Inventory;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Nodes;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Skills;

namespace Nosum.Infrastructure.Combat;

/// <summary>
/// Server-authoritative implementation of <see cref="ICombatService"/> (docs/adr/ADR-045 D3).
/// </summary>
public sealed class CombatService : ICombatService, IDisposable
{
    private readonly IMapPresence _presence;
    private readonly IMapCatalog _maps;
    private readonly IInventoryService _inventory;
    private readonly IGroundItems _groundItems;
    private readonly IGameDayClock _dayClock;
    private readonly IPlayerNotifier? _notifier;

    private readonly ConcurrentDictionary<Guid, PlayerVitalsState> _vitals = new();
    private readonly ConcurrentDictionary<string, InstanceMonsters> _instances = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="CombatService"/> class.</summary>
    public CombatService(
        IMapPresence presence,
        IMapCatalog maps,
        IInventoryService inventory,
        IGroundItems groundItems,
        IGameDayClock dayClock,
        IPlayerNotifier? notifier = null)
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
        _presence.PlayerLeft += this.OnPlayerLeftAsync;
    }

    /// <inheritdoc/>
    public void Dispose() => _presence.PlayerLeft -= this.OnPlayerLeftAsync;

    /// <inheritdoc/>
    public ValueTask<VitalsView> GetVitalsAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        PlayerVitalsState state = this.GetOrCreateVitals(playerId);
        lock (state.Gate)
        {
            return ValueTask.FromResult(state.ToView());
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

            return ValueTask.FromResult(new MonstersView(player.MapId, alive));
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

        PlayerVitalsState vitals = this.GetOrCreateVitals(player.AccountId);
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

        InstanceMonsters instance = this.GetOrCreateInstance(player);
        MonsterSpawn updatedMonster;
        int damageDealt;
        int damageTaken = 0;
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
            if (dist > range)
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
                damageDealt = CombatRules.CalculateDamage(held.ItemId, oldLevel);
                int remainingHp = Math.Max(0, target.Hp - damageDealt);
                updatedMonster = target with { Hp = remainingHp };
                instance.Monsters[idx] = updatedMonster;
                defeated = remainingHp == 0;

                _ = MonsterCatalog.TryGet(target.Kind, out MonsterDefinition? def);
                int xpGain = defeated ? (def?.XpReward ?? 15) : SkillRules.HitXp;
                vitals.CombatXp = Math.Min(SkillRules.XpCap, vitals.CombatXp + xpGain);
                newLevel = SkillRules.LevelOf(vitals.CombatXp);

                if (newLevel > oldLevel)
                {
                    int nextMaxHp = CombatRules.MaxHpForLevel(newLevel);
                    int nextMaxMp = CombatRules.MaxMpForLevel(newLevel);
                    vitals.Hp = Math.Min(nextMaxHp, vitals.Hp + (nextMaxHp - vitals.MaxHp));
                    vitals.Mp = Math.Min(nextMaxMp, vitals.Mp + (nextMaxMp - vitals.MaxMp));
                    vitals.MaxHp = nextMaxHp;
                    vitals.MaxMp = nextMaxMp;
                }

                if (!defeated && def is not null && def.AttackDamage > 0)
                {
                    damageTaken = def.AttackDamage;
                    vitals.Hp -= damageTaken;
                    if (vitals.Hp <= 0)
                    {
                        knockedOut = true;
                        vitals.Hp = Math.Max(1, vitals.MaxHp / 2);
                    }
                }

                updatedVitals = vitals.ToView();
            }
        }

        await this.BroadcastMonsterChangedAsync(player.Key, updatedMonster, cancellationToken).ConfigureAwait(false);

        if (defeated && MonsterCatalog.TryGet(updatedMonster.Kind, out MonsterDefinition? defeatedDef))
        {
            int dropPixelX = (updatedMonster.X * tileSize) + (tileSize / 2);
            int dropPixelY = (updatedMonster.Y * tileSize) + (tileSize / 2);
            _ = await _groundItems.DropAsync(
                new GroundDrop(player.Instance, dropPixelX, dropPixelY, defeatedDef.PrimaryDropItemId, 1, player.AccountId),
                cancellationToken).ConfigureAwait(false);

            if (((updatedMonster.MonsterId + updatedMonster.X + updatedMonster.Y) & 1) == 0 && defeatedDef.RareDropItemId > 0)
            {
                _ = await _groundItems.DropAsync(
                    new GroundDrop(player.Instance, dropPixelX + 4, dropPixelY, defeatedDef.RareDropItemId, 1, player.AccountId),
                    cancellationToken).ConfigureAwait(false);
            }
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
            knockedOut);
    }

    /// <inheritdoc/>
    public async Task<DrinkPotionOutcome> DrinkPotionAsync(IConnection connection, int slot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player))
        {
            return new DrinkPotionOutcome(CombatStatus.NotJoined, default);
        }

        PlayerVitalsState vitals = this.GetOrCreateVitals(player.AccountId);
        if ((uint)slot >= (uint)InventoryLimits.DefaultCapacity)
        {
            lock (vitals.Gate)
            {
                return new DrinkPotionOutcome(CombatStatus.WrongItem, vitals.ToView());
            }
        }

        InventorySlotView held = await _inventory.PeekSlotAsync(player.AccountId, slot, cancellationToken).ConfigureAwait(false);
        (int hpRestore, int mpRestore) = CombatRules.PotionRestore(held.ItemId);
        if (held.Quantity <= 0 || (hpRestore <= 0 && mpRestore <= 0))
        {
            lock (vitals.Gate)
            {
                return new DrinkPotionOutcome(CombatStatus.WrongItem, vitals.ToView());
            }
        }

        lock (vitals.Gate)
        {
            if (vitals.Hp >= vitals.MaxHp && vitals.Mp >= vitals.MaxMp)
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
            updated = vitals.ToView();
        }

        return new DrinkPotionOutcome(CombatStatus.Ok, updated);
    }

    private PlayerVitalsState GetOrCreateVitals(Guid playerId)
        => _vitals.GetOrAdd(playerId, static _ => new PlayerVitalsState());

    private InstanceMonsters GetOrCreateInstance(PlayerPresence player)
    {
        long today = _dayClock.Today;
        string key = $"{player.Key}:{today}";
        return _instances.GetOrAdd(key, _ =>
        {
            if (!_maps.TryGet(player.MapId, out MapDefinition? map))
            {
                return new InstanceMonsters([]);
            }

            int floor = GameMaps.TryGet(player.MapId, out MapSpec? spec) ? spec.MineFloor : 0;
            ulong seed = floor >= 1
                ? MineRules.FloorSeed(player.AccountId, today, floor)
                : NodeRandom.Mix(NodeRandom.HashOf(player.MapId), today, 0x4D4F4E);

            HashSet<(int X, int Y)> occupied = [];
            int tileSize = Math.Max(1, map.Grid.TileSize);
            int spawnX = (int)(map.SpawnX / tileSize);
            int spawnY = (int)(map.SpawnY / tileSize);
            List<MonsterSpawn> planned = CombatRules.PlanMonsters(player.MapId, seed, map.Grid, occupied, spawnX, spawnY);
            return new InstanceMonsters(planned);
        });
    }

    private async ValueTask BroadcastMonsterChangedAsync(string instanceKey, MonsterSpawn monster, CancellationToken cancellationToken)
    {
        List<PlayerPresence> present = [];
        _presence.CopyPlayers(instanceKey, present);
        if (present.Count == 0 || _notifier is null)
        {
            return;
        }

        foreach (PlayerPresence p in present)
        {
            using PacketScope<MonsterChanged> lease = PacketFactory<MonsterChanged>.Acquire();
            lease.Value.MonsterId = monster.MonsterId;
            lease.Value.Kind = (byte)monster.Kind;
            lease.Value.X = (ushort)monster.X;
            lease.Value.Y = (ushort)monster.Y;
            lease.Value.Hp = (ushort)monster.Hp;
            lease.Value.MaxHp = (ushort)monster.MaxHp;
            _ = await _notifier.TrySendAsync(p.AccountId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private ValueTask OnPlayerLeftAsync(PlayerPresence player)
    {
        if (!player.Instance.IsPrivate)
        {
            return ValueTask.CompletedTask;
        }

        List<PlayerPresence> remaining = [];
        _presence.CopyPlayers(player.Key, remaining);
        if (remaining.Count == 0)
        {
            long today = _dayClock.Today;
            _ = _instances.TryRemove($"{player.Key}:{today}", out _);
        }

        return ValueTask.CompletedTask;
    }

    private sealed class PlayerVitalsState
    {
        public object Gate { get; } = new();

        public int Hp { get; set; } = CombatRules.BaseMaxHp;

        public int MaxHp { get; set; } = CombatRules.BaseMaxHp;

        public int Mp { get; set; } = CombatRules.BaseMaxMp;

        public int MaxMp { get; set; } = CombatRules.BaseMaxMp;

        public int CombatXp { get; set; }

        public VitalsView ToView() => new(Hp, MaxHp, Mp, MaxMp, CombatXp);
    }

    private sealed class InstanceMonsters
    {
        public InstanceMonsters(List<MonsterSpawn> monsters) => Monsters = monsters;

        public object Gate { get; } = new();

        public List<MonsterSpawn> Monsters { get; }
    }
}
