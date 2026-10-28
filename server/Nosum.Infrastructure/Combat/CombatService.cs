// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.S2C;
using PlayerState = Nosum.Domain.Accounts.PlayerState;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Energy;
using Nosum.Gameplay.Inventory;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Looks;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Mines;
using Nosum.Gameplay.Nodes;
using Nosum.Gameplay.Skills;
using Nosum.Infrastructure.Concurrency;
using Nosum.Infrastructure.Progress;

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
    private readonly ILookService? _looks;
    private readonly IPlayerService? _players;
    private readonly IUnitOfWorkFactory? _unitOfWork;
    private readonly PlayerStateCache? _stateCache;
    private readonly ICoopMembership? _coops;
    private readonly IPlayerProgress? _progress;

    private readonly ConcurrentDictionary<Guid, PlayerVitalsState> _vitals = new();
    private readonly ConcurrentDictionary<string, InstanceMonsters> _instances = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(Guid PlayerId, byte SkillId), long> _skillCooldowns = new();
    private readonly ConcurrentDictionary<(Guid PlayerId, string InstanceKey, byte ShrineId), long> _shrineCooldowns = new();
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
        IPlayerProgress? progress = null)
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
        if (_progress is PlayerProgressService progressService)
        {
            progressService.Combat = this;
        }

        _presence.PlayerLeft += this.OnPlayerLeftAsync;
    }

    /// <summary>Evicts per-player transient combat caches when a player disconnects.</summary>
    /// <param name="playerId">The disconnected player ID.</param>
    public void OnPlayerDisconnected(Guid playerId)
    {
        _ = _vitals.TryRemove(playerId, out _);
        _ = _playerCombos.TryRemove(playerId, out _);

        foreach ((Guid PlayerId, byte SkillId) key in _skillCooldowns.Keys)
        {
            if (key.PlayerId == playerId)
            {
                _ = _skillCooldowns.TryRemove(key, out _);
            }
        }

        foreach ((Guid PlayerId, string InstanceKey, byte ShrineId) key in _shrineCooldowns.Keys)
        {
            if (key.PlayerId == playerId)
            {
                _ = _shrineCooldowns.TryRemove(key, out _);
            }
        }
    }

    /// <summary>The time between monster AI decisions (docs/adr/ADR-046 D1).</summary>
    public static readonly TimeSpan DefaultAiInterval = TimeSpan.FromMilliseconds(1500);

    /// <summary>Runs the monster AI tick on active map instances with players (docs/adr/ADR-046 D1).</summary>
    /// <param name="interval">Tick interval.</param>
    /// <param name="onError">Error callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that runs until cancelled.</returns>
    [SuppressMessage("Design", "CA1031", Justification = "AI loop runs indefinitely and reports unexpected errors to callback.")]
    public async Task RunAiAsync(TimeSpan interval, Action<Exception>? onError = null, CancellationToken cancellationToken = default)
    {
        using PeriodicTimer timer = new(interval);
        List<PlayerPresence> players = new(16);
        List<MonsterSpawn> moved = new(8);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                long today = _dayClock.Today;
                foreach (KeyValuePair<string, InstanceMonsters> entry in _instances)
                {
                    string instanceKeyWithDate = entry.Key;
                    int colon = instanceKeyWithDate.LastIndexOf(':');
                    if (colon > 0 && long.TryParse(instanceKeyWithDate.AsSpan(colon + 1), out long instanceDay) && instanceDay != today)
                    {
                        _ = _instances.TryRemove(instanceKeyWithDate, out _);
                        continue;
                    }

                    string instanceKey = colon > 0 ? instanceKeyWithDate[..colon] : instanceKeyWithDate;

                    players.Clear();
                    _presence.CopyPlayers(instanceKey, players);
                    if (players.Count == 0)
                    {
                        continue;
                    }

                    string mapId = players[0].MapId;
                    if (!_maps.TryGet(mapId, out MapDefinition? map))
                    {
                        continue;
                    }

                    InstanceMonsters instance = entry.Value;
                    moved.Clear();
                    lock (instance.Gate)
                    {
                        int tileSize = Math.Max(1, map.Grid.TileSize);
                        for (int i = 0; i < instance.Monsters.Count; i++)
                        {
                            MonsterSpawn m = instance.Monsters[i];
                            if (m.Hp <= 0)
                            {
                                continue;
                            }

                            int bestDist = int.MaxValue;
                            PlayerPresence? nearest = null;
                            int targetTileX = 0;
                            int targetTileY = 0;

                            for (int pIdx = 0; pIdx < players.Count; pIdx++)
                            {
                                PlayerPresence p = players[pIdx];
                                double px;
                                double py;
                                lock (p.Gate)
                                {
                                    px = p.Motion.X;
                                    py = p.Motion.Y;
                                }

                                int pTileX = (int)Math.Floor(px / tileSize);
                                int pTileY = (int)Math.Floor(py / tileSize);
                                int d = Math.Max(Math.Abs(pTileX - m.X), Math.Abs(pTileY - m.Y));
                                if (d < bestDist)
                                {
                                    bestDist = d;
                                    nearest = p;
                                    targetTileX = pTileX;
                                    targetTileY = pTileY;
                                }
                            }

                            int aggroLimit = CombatRules.IsBoss(m.Kind) ? CombatRules.BossAggroRadius : CombatRules.AggroRadius;
                            if (nearest is not null && bestDist <= aggroLimit)
                            {
                                if (bestDist <= 1)
                                {
                                    int damage = CombatRules.MonsterDamage(m.Kind);
                                    _ = this.ApplyMonsterContactAttackAsync(nearest, damage, m.Kind, instanceKey, instance, m.MonsterId, cancellationToken);
                                }
                                else
                                {
                                    int stepX = m.X + Math.Sign(targetTileX - m.X);
                                    int stepY = m.Y + Math.Sign(targetTileY - m.Y);
                                    if (!map.Grid.IsBlocked(stepX, stepY))
                                    {
                                        MonsterSpawn updated = m with { X = stepX, Y = stepY };
                                        if (m.Modifier == MonsterModifier.Swift)
                                        {
                                            int distAfterFirstStep = Math.Max(Math.Abs(targetTileX - updated.X), Math.Abs(targetTileY - updated.Y));
                                            if (distAfterFirstStep > 1)
                                            {
                                                int step2X = updated.X + Math.Sign(targetTileX - updated.X);
                                                int step2Y = updated.Y + Math.Sign(targetTileY - updated.Y);
                                                if (!map.Grid.IsBlocked(step2X, step2Y))
                                                {
                                                    updated = updated with { X = step2X, Y = step2Y };
                                                }
                                            }
                                        }

                                        instance.Monsters[i] = updated;
                                        moved.Add(updated);
                                    }
                                }
                            }
                            else if (Random.Shared.Next(4) == 0)
                            {
                                int dir = Random.Shared.Next(4);
                                int wx = m.X + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                                int wy = m.Y + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                                if (!map.Grid.IsBlocked(wx, wy))
                                {
                                    MonsterSpawn updated = m with { X = wx, Y = wy };
                                    instance.Monsters[i] = updated;
                                    moved.Add(updated);
                                }
                            }
                        }

                        if (instance.Hazards.Count > 0)
                        {
                            long now = Environment.TickCount64;
                            for (int hIdx = 0; hIdx < instance.Hazards.Count; hIdx++)
                            {
                                DungeonHazard h = instance.Hazards[hIdx];
                                bool isActive = h.Kind switch
                                {
                                    DungeonHazardKind.SpikeTrap => ((now + (h.HazardId * 700)) % 2400) > 1560,
                                    _ => true,
                                };

                                if (!isActive)
                                {
                                    continue;
                                }

                                for (int pIdx = 0; pIdx < players.Count; pIdx++)
                                {
                                    PlayerPresence p = players[pIdx];
                                    double px;
                                    double py;
                                    lock (p.Gate)
                                    {
                                        px = p.Motion.X;
                                        py = p.Motion.Y;
                                    }

                                    int pTileX = (int)Math.Floor(px / tileSize);
                                    int pTileY = (int)Math.Floor(py / tileSize);
                                    if (CombatRules.IsShrine(h.Kind))
                                    {
                                        if (pTileX == h.X && pTileY == h.Y)
                                        {
                                            _ = this.ApplyShrineBlessingAsync(p.AccountId, instanceKey, instance, h, now, cancellationToken);
                                        }
                                    }
                                    else if (pTileX == h.X && pTileY == h.Y)
                                    {
                                        _ = this.ApplyHazardContactAsync(p, h.Damage, h.Kind, cancellationToken);
                                    }
                                }
                            }
                        }
                    }

                    for (int mIdx = 0; mIdx < moved.Count; mIdx++)
                    {
                        await this.BroadcastMonsterChangedAsync(instanceKey, moved[mIdx], cancellationToken).ConfigureAwait(false);
                    }
                }

                foreach (KeyValuePair<Guid, PlayerVitalsState> kvp in _vitals)
                {
                    PlayerVitalsState state = kvp.Value;
                    bool changed = false;
                    bool knockedOut = false;
                    VitalsView view;
                    lock (state.Gate)
                    {
                        if (state.EffectRemainingTicks > 0)
                        {
                            state.EffectRemainingTicks--;
                            if (state.ActiveEffect == StatusEffectKind.Regeneration)
                            {
                                int nextHp = Math.Min(state.MaxHp, state.Hp + state.EffectMagnitude);
                                if (nextHp != state.Hp)
                                {
                                    state.Hp = nextHp;
                                    changed = true;
                                }
                            }
                            else if (state.ActiveEffect == StatusEffectKind.Poison)
                            {
                                int nextHp = Math.Max(0, state.Hp - state.EffectMagnitude);
                                if (nextHp != state.Hp)
                                {
                                    state.Hp = nextHp;
                                    changed = true;
                                }

                                if (state.Hp == 0)
                                {
                                    knockedOut = true;
                                    state.Hp = CombatRules.ReviveHp(state.MaxHp);
                                    state.ActiveEffect = StatusEffectKind.None;
                                    state.EffectRemainingTicks = 0;
                                    state.EffectMagnitude = 0;
                                }
                            }

                            if (!knockedOut && state.EffectRemainingTicks <= 0)
                            {
                                state.ActiveEffect = StatusEffectKind.None;
                                changed = true;
                            }
                        }

                        view = state.ToView();
                    }

                    if (knockedOut)
                    {
                        await this.SaveVitalsAsync(kvp.Key, view, cancellationToken).ConfigureAwait(false);
                        await this.CompleteKnockoutAsync(this.FindPresence(kvp.Key), kvp.Key, view, cancellationToken).ConfigureAwait(false);
                    }
                    else if (changed)
                    {
                        await this.SaveVitalsAsync(kvp.Key, view, cancellationToken).ConfigureAwait(false);
                        if (_notifier is not null)
                        {
                            using PacketScope<VitalsState> lease = PacketFactory<VitalsState>.Acquire();
                            lease.Value.Hp = (ushort)view.Hp;
                            lease.Value.MaxHp = (ushort)view.MaxHp;
                            lease.Value.Mp = (ushort)view.Mp;
                            lease.Value.MaxMp = (ushort)view.MaxMp;
                            lease.Value.CombatXp = view.CombatXp;
                            lease.Value.StatusEffect = view.StatusEffect;
                            _ = await _notifier.TrySendAsync(kvp.Key, lease.Value, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }
    }

    private PlayerPresence? FindPresence(Guid accountId)
    {
        List<string> keys = [];
        _presence.CopyActiveMapIds(keys);
        List<PlayerPresence> list = [];
        foreach (string key in keys)
        {
            list.Clear();
            _presence.CopyPlayers(key, list);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].AccountId == accountId)
                {
                    return list[i];
                }
            }
        }

        return null;
    }

    private async Task CompleteKnockoutAsync(PlayerPresence? player, Guid playerId, VitalsView revivedView, CancellationToken cancellationToken)
    {
        _ = _playerCombos.TryRemove(playerId, out _);

        if (_notifier is not null)
        {
            using PacketScope<VitalsState> zeroLease = PacketFactory<VitalsState>.Acquire();
            zeroLease.Value.Hp = 0;
            zeroLease.Value.MaxHp = (ushort)revivedView.MaxHp;
            zeroLease.Value.Mp = (ushort)revivedView.Mp;
            zeroLease.Value.MaxMp = (ushort)revivedView.MaxMp;
            zeroLease.Value.CombatXp = revivedView.CombatXp;
            zeroLease.Value.StatusEffect = 0;
            _ = await _notifier.TrySendAsync(playerId, zeroLease.Value, cancellationToken).ConfigureAwait(false);
        }

        if (_players is not null && player is not null && !string.Equals(player.MapId, GameMaps.FarmHouse, StringComparison.Ordinal))
        {
            TravelStatus status = await _players.TravelAsync(player.Connection, new MapInstance(GameMaps.FarmHouse, player.AccountId), cancellationToken).ConfigureAwait(false);
            if (status != TravelStatus.Arrived && !string.Equals(player.MapId, GameMaps.Farm, StringComparison.Ordinal))
            {
                _ = await _players.TravelAsync(player.Connection, new MapInstance(GameMaps.Farm, player.AccountId), cancellationToken).ConfigureAwait(false);
            }
        }

        if (_notifier is not null)
        {
            using PacketScope<VitalsState> lease = PacketFactory<VitalsState>.Acquire();
            lease.Value.Hp = (ushort)revivedView.Hp;
            lease.Value.MaxHp = (ushort)revivedView.MaxHp;
            lease.Value.Mp = (ushort)revivedView.Mp;
            lease.Value.MaxMp = (ushort)revivedView.MaxMp;
            lease.Value.CombatXp = revivedView.CombatXp;
            lease.Value.StatusEffect = revivedView.StatusEffect;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyMonsterContactAttackAsync(
        PlayerPresence player,
        int damage,
        MonsterKind monsterKind,
        string instanceKey,
        InstanceMonsters instance,
        int monsterId,
        CancellationToken cancellationToken)
    {
        Guid playerId = player.AccountId;
        PlayerVitalsState state = await this.GetOrCreateVitalsAsync(playerId, cancellationToken).ConfigureAwait(false);

        int defense = 0;
        if (_looks is not null)
        {
            PlayerLook look = await _looks.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
            defense = CombatRules.CalculateDefense(look.HatItemId, look.ShirtItemId, look.PantsItemId);
        }

        int finalDamage = CombatRules.MitigateDamage(damage, defense);
        (StatusEffectKind debuff, int durationSecs, int tickMag) = CombatRules.MonsterStatusOnHit(monsterKind);
        VitalsView updated;
        bool knockedOut = false;
        lock (state.Gate)
        {
            state.Hp = Math.Max(0, state.Hp - finalDamage);
            if (state.Hp == 0)
            {
                knockedOut = true;
                state.Hp = CombatRules.ReviveHp(state.MaxHp);
                state.ActiveEffect = StatusEffectKind.None;
                state.EffectRemainingTicks = 0;
                state.EffectMagnitude = 0;
            }
            else if (debuff != StatusEffectKind.None)
            {
                state.ActiveEffect = debuff;
                state.EffectRemainingTicks = durationSecs / 2;
                state.EffectMagnitude = tickMag;
            }

            updated = state.ToView();
        }

        if (finalDamage > 0)
        {
            MonsterSpawn? vampiricHealed = null;
            lock (instance.Gate)
            {
                for (int i = 0; i < instance.Monsters.Count; i++)
                {
                    MonsterSpawn m = instance.Monsters[i];
                    if (m.MonsterId == monsterId && m.Hp > 0 && m.Modifier == MonsterModifier.Vampiric && m.Hp < m.MaxHp)
                    {
                        int heal = Math.Max(1, finalDamage / 2);
                        MonsterSpawn nextM = m with { Hp = Math.Min(m.MaxHp, m.Hp + heal) };
                        instance.Monsters[i] = nextM;
                        vampiricHealed = nextM;
                        break;
                    }
                }
            }

            if (vampiricHealed is { } healedMonster)
            {
                await this.BroadcastMonsterChangedAsync(instanceKey, healedMonster, cancellationToken).ConfigureAwait(false);
            }
        }

        await this.SaveVitalsAsync(playerId, updated, cancellationToken).ConfigureAwait(false);

        if (knockedOut)
        {
            await this.CompleteKnockoutAsync(player, playerId, updated, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_notifier is not null)
        {
            using PacketScope<VitalsState> lease = PacketFactory<VitalsState>.Acquire();
            lease.Value.Hp = (ushort)updated.Hp;
            lease.Value.MaxHp = (ushort)updated.MaxHp;
            lease.Value.Mp = (ushort)updated.Mp;
            lease.Value.MaxMp = (ushort)updated.MaxMp;
            lease.Value.CombatXp = updated.CombatXp;
            lease.Value.StatusEffect = updated.StatusEffect;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyHazardContactAsync(PlayerPresence player, int damage, DungeonHazardKind hazardKind, CancellationToken cancellationToken)
    {
        Guid playerId = player.AccountId;
        PlayerVitalsState state = await this.GetOrCreateVitalsAsync(playerId, cancellationToken).ConfigureAwait(false);

        int defense = 0;
        if (_looks is not null)
        {
            PlayerLook look = await _looks.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
            defense = CombatRules.CalculateDefense(look.HatItemId, look.ShirtItemId, look.PantsItemId);
        }

        int finalDamage = CombatRules.MitigateDamage(damage, defense);
        VitalsView updated;
        bool knockedOut = false;
        lock (state.Gate)
        {
            state.Hp = Math.Max(0, state.Hp - finalDamage);
            if (state.Hp == 0)
            {
                knockedOut = true;
                state.Hp = CombatRules.ReviveHp(state.MaxHp);
                state.ActiveEffect = StatusEffectKind.None;
                state.EffectRemainingTicks = 0;
                state.EffectMagnitude = 0;
            }
            else if (hazardKind == DungeonHazardKind.SporeVent)
            {
                state.ActiveEffect = StatusEffectKind.Poison;
                state.EffectRemainingTicks = 4;
                state.EffectMagnitude = 3;
            }

            updated = state.ToView();
        }

        await this.SaveVitalsAsync(playerId, updated, cancellationToken).ConfigureAwait(false);

        if (knockedOut)
        {
            await this.CompleteKnockoutAsync(player, playerId, updated, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_notifier is not null)
        {
            using PacketScope<VitalsState> lease = PacketFactory<VitalsState>.Acquire();
            lease.Value.Hp = (ushort)updated.Hp;
            lease.Value.MaxHp = (ushort)updated.MaxHp;
            lease.Value.Mp = (ushort)updated.Mp;
            lease.Value.MaxMp = (ushort)updated.MaxMp;
            lease.Value.CombatXp = updated.CombatXp;
            lease.Value.StatusEffect = updated.StatusEffect;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyShrineBlessingAsync(
        Guid playerId,
        string instanceKey,
        InstanceMonsters instance,
        DungeonHazard shrine,
        long now,
        CancellationToken cancellationToken)
    {
        if (_shrineCooldowns.TryGetValue((playerId, instanceKey, shrine.HazardId), out long readyAt) && now < readyAt)
        {
            return;
        }

        _shrineCooldowns[(playerId, instanceKey, shrine.HazardId)] = now + CombatRules.ShrineCooldownMs;
        PlayerVitalsState state = await this.GetOrCreateVitalsAsync(playerId, cancellationToken).ConfigureAwait(false);
        (int healHp, int restoreMp, int bonusXp, StatusEffectKind buff) = CombatRules.ShrineBlessing(shrine.Kind);

        List<MonsterSpawn>? struckMonsters = null;
        if (CombatRules.IsResonancePylon(shrine.Kind))
        {
            (int monsterDamage, int radiusTiles, _) = CombatRules.PylonDischarge(shrine.Kind);
            if (monsterDamage > 0 && radiusTiles > 0)
            {
                lock (instance.Gate)
                {
                    for (int i = 0; i < instance.Monsters.Count; i++)
                    {
                        MonsterSpawn m = instance.Monsters[i];
                        if (m.Hp <= 0)
                        {
                            continue;
                        }

                        int dist = Math.Max(Math.Abs(m.X - shrine.X), Math.Abs(m.Y - shrine.Y));
                        if (dist > radiusTiles)
                        {
                            continue;
                        }

                        int nextHp = Math.Max(0, m.Hp - monsterDamage);
                        MonsterSpawn updatedMonster = m with { Hp = nextHp };
                        instance.Monsters[i] = updatedMonster;
                        struckMonsters ??= [];
                        struckMonsters.Add(updatedMonster);

                        if (nextHp == 0 && MonsterCatalog.TryGet(m.Kind, out MonsterDefinition? def))
                        {
                            int killXp = m.Modifier != MonsterModifier.None ? (int)(def.XpReward * 2.5) : def.XpReward;
                            bonusXp += killXp;
                        }
                    }
                }
            }
        }

        VitalsView updated;
        int oldLevel;
        int newLevel;
        lock (state.Gate)
        {
            oldLevel = SkillRules.LevelOf(state.CombatXp);
            if (healHp > 0)
            {
                state.Hp = Math.Min(state.MaxHp, state.Hp + healHp);
            }

            if (restoreMp > 0)
            {
                state.Mp = Math.Min(state.MaxMp, state.Mp + restoreMp);
            }

            if (bonusXp > 0)
            {
                state.CombatXp = Math.Min(SkillRules.XpCap, state.CombatXp + bonusXp);
            }

            if (buff != StatusEffectKind.None)
            {
                state.ActiveEffect = buff;
                state.EffectRemainingTicks = 3;
                state.EffectMagnitude = buff == StatusEffectKind.Regeneration ? 2 : 0;
            }

            newLevel = SkillRules.LevelOf(state.CombatXp);
            if (newLevel > oldLevel)
            {
                int nextMaxHp = CombatRules.MaxHpForLevel(newLevel);
                int nextMaxMp = CombatRules.MaxMpForLevel(newLevel);
                int hpBonus = Math.Max(0, nextMaxHp - state.MaxHp);
                int mpBonus = Math.Max(0, nextMaxMp - state.MaxMp);
                state.MaxHp = nextMaxHp;
                state.MaxMp = nextMaxMp;
                state.Hp = Math.Min(nextMaxHp, state.Hp + hpBonus);
                state.Mp = Math.Min(nextMaxMp, state.Mp + mpBonus);
            }

            updated = state.ToView();
        }

        await this.SaveVitalsAsync(playerId, updated, cancellationToken).ConfigureAwait(false);

        if (struckMonsters is not null)
        {
            PlayerPresence? killerPresence = this.FindPresence(playerId);
            int tileSize = killerPresence is not null && _maps.TryGet(killerPresence.MapId, out MapDefinition? killerMap)
                ? Math.Max(1, killerMap.Grid.TileSize)
                : 16;
            MapInstance mapInst = killerPresence?.Instance ?? MapInstance.Shared(instanceKey);
            for (int i = 0; i < struckMonsters.Count; i++)
            {
                MonsterSpawn struck = struckMonsters[i];
                await this.BroadcastMonsterChangedAsync(instanceKey, struck, cancellationToken).ConfigureAwait(false);
                if (struck.Hp == 0)
                {
                    await this.HandleMonsterDefeatedAsync(killerPresence, playerId, instanceKey, mapInst, tileSize, struck, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        if (_notifier is not null)
        {
            using PacketScope<VitalsState> lease = PacketFactory<VitalsState>.Acquire();
            lease.Value.Hp = (ushort)updated.Hp;
            lease.Value.MaxHp = (ushort)updated.MaxHp;
            lease.Value.Mp = (ushort)updated.Mp;
            lease.Value.MaxMp = (ushort)updated.MaxMp;
            lease.Value.CombatXp = updated.CombatXp;
            lease.Value.StatusEffect = updated.StatusEffect;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);

            if (newLevel > oldLevel)
            {
                using PacketScope<LevelUp> levelLease = PacketFactory<LevelUp>.Acquire();
                levelLease.Value.Skill = (byte)SkillKind.Combat;
                levelLease.Value.Level = (byte)newLevel;
                _ = await _notifier.TrySendAsync(playerId, levelLease.Value, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _presence.PlayerLeft -= this.OnPlayerLeftAsync;

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

                if (!defeated && def is not null && def.AttackDamage > 0)
                {
                    damageTaken = CombatRules.MitigateDamage(def.AttackDamage, playerDefense);
                    vitals.Hp -= damageTaken;
                    if (damageTaken > 0 && updatedMonster.Modifier == MonsterModifier.Vampiric && updatedMonster.Hp < updatedMonster.MaxHp)
                    {
                        int vampHeal = Math.Max(1, damageTaken / 2);
                        updatedMonster = updatedMonster with { Hp = Math.Min(updatedMonster.MaxHp, updatedMonster.Hp + vampHeal) };
                    }
                }

                instance.Monsters[idx] = updatedMonster;

                if (vitals.Hp <= 0)
                {
                    knockedOut = true;
                    vitals.Hp = CombatRules.ReviveHp(vitals.MaxHp);
                    vitals.ActiveEffect = StatusEffectKind.None;
                    vitals.EffectRemainingTicks = 0;
                    vitals.EffectMagnitude = 0;
                }

                updatedVitals = vitals.ToView();
            }
        }

        await this.SaveVitalsAsync(player.AccountId, updatedVitals, cancellationToken).ConfigureAwait(false);
        await this.BroadcastMonsterChangedAsync(player.Key, updatedMonster, cancellationToken).ConfigureAwait(false);

        if (knockedOut)
        {
            await this.CompleteKnockoutAsync(player, player.AccountId, updatedVitals, cancellationToken).ConfigureAwait(false);
        }

        if (defeated)
        {
            await this.HandleMonsterDefeatedAsync(player, player.AccountId, player.Key, player.Instance, tileSize, updatedMonster, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Checks whether consuming the given potion item would restore HP, MP, or apply a status effect for the player.</summary>
    internal async ValueTask<bool> CanBenefitFromPotionAsync(Guid playerId, int itemId, CancellationToken cancellationToken = default)
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

    /// <summary>Applies the HP, MP, and status effect of a potion consumed via <c>EatFood</c> and notifies the player.</summary>
    internal async ValueTask ApplyConsumedPotionEffectsAsync(Guid playerId, int itemId, CancellationToken cancellationToken = default)
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
            if (effect != StatusEffectKind.None)
            {
                vitals.ActiveEffect = effect;
                vitals.EffectRemainingTicks = durationSeconds / 2;
                vitals.EffectMagnitude = magnitude;
            }

            updated = vitals.ToView();
        }

        await this.SaveVitalsAsync(playerId, updated, cancellationToken).ConfigureAwait(false);
        if (_notifier is not null)
        {
            using PacketScope<VitalsState> lease = PacketFactory<VitalsState>.Acquire();
            lease.Value.Hp = (ushort)updated.Hp;
            lease.Value.MaxHp = (ushort)updated.MaxHp;
            lease.Value.Mp = (ushort)updated.Mp;
            lease.Value.MaxMp = (ushort)updated.MaxMp;
            lease.Value.CombatXp = updated.CombatXp;
            lease.Value.StatusEffect = updated.StatusEffect;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
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
            && _progress is PlayerProgressService progressCheck
            && await progressCheck.IsEnergyBelowMaxAsync(player.AccountId, cancellationToken).ConfigureAwait(false);

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
            if (effect != StatusEffectKind.None)
            {
                vitals.ActiveEffect = effect;
                vitals.EffectRemainingTicks = durationSeconds / 2;
                vitals.EffectMagnitude = magnitude;
            }

            updated = vitals.ToView();
        }

        await this.SaveVitalsAsync(player.AccountId, updated, cancellationToken).ConfigureAwait(false);

        if (foodEnergy > 0 && _progress is PlayerProgressService progressService)
        {
            await progressService.RestoreEnergyFromPotionAsync(player.AccountId, foodEnergy, cancellationToken).ConfigureAwait(false);
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
            _ = await _notifier.TrySendAsync(player.AccountId, lease.Value, cancellationToken).ConfigureAwait(false);
        }

        return new DrinkPotionOutcome(CombatStatus.Ok, updated);
    }

    /// <inheritdoc/>
    public async Task<CastSkillOutcome> CastSkillAsync(IConnection connection, byte skillId, int targetMonsterId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map))
        {
            return new CastSkillOutcome(CombatStatus.NotJoined, skillId, 0, 0, 0, default, 0);
        }

        if (!SkillCatalog.TryGet((CombatSkillKind)skillId, out SkillDefinition? skill))
        {
            return new CastSkillOutcome(CombatStatus.UnknownSkill, skillId, 0, 0, 0, default, 0);
        }

        PlayerVitalsState vitals = await this.GetOrCreateVitalsAsync(player.AccountId, cancellationToken).ConfigureAwait(false);
        long now = Environment.TickCount64;
        if (_skillCooldowns.TryGetValue((player.AccountId, skillId), out long readyAt) && now < readyAt)
        {
            lock (vitals.Gate)
            {
                return new CastSkillOutcome(CombatStatus.OnCooldown, skillId, 0, 0, 0, vitals.ToView(), (int)Math.Max(0, readyAt - now));
            }
        }

        lock (vitals.Gate)
        {
            if (vitals.Mp < skill.MpCost)
            {
                return new CastSkillOutcome(CombatStatus.NotEnoughMp, skillId, 0, 0, 0, vitals.ToView(), 0);
            }
        }

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
        int totalDamage = 0;
        byte targetsHit = 0;
        int healAmount = 0;
        List<MonsterSpawn> broadcastChanges = new(8);
        int oldLevel;
        int newLevel;
        VitalsView resultVitals;

        lock (instance.Gate)
        {
            lock (vitals.Gate)
            {
                if (vitals.Mp < skill.MpCost)
                {
                    return new CastSkillOutcome(CombatStatus.NotEnoughMp, skillId, 0, 0, 0, vitals.ToView(), 0);
                }

                oldLevel = SkillRules.LevelOf(vitals.CombatXp);
                int scaledPower = skill.BasePower + (oldLevel * 2);

                if (skill.Kind == CombatSkillKind.DivineHeal)
                {
                    if (vitals.Hp >= vitals.MaxHp)
                    {
                        return new CastSkillOutcome(CombatStatus.AlreadyFull, skillId, 0, 0, 0, vitals.ToView(), 0);
                    }

                    vitals.Mp -= skill.MpCost;
                    int oldHp = vitals.Hp;
                    vitals.Hp = Math.Min(vitals.MaxHp, vitals.Hp + scaledPower);
                    healAmount = vitals.Hp - oldHp;
                }
                else if (skill.Kind == CombatSkillKind.Whirlwind)
                {
                    bool anyInRange = false;
                    for (int i = 0; i < instance.Monsters.Count; i++)
                    {
                        MonsterSpawn mCheck = instance.Monsters[i];
                        if (mCheck.Hp > 0 && Math.Max(Math.Abs(playerTileX - mCheck.X), Math.Abs(playerTileY - mCheck.Y)) <= skill.Range)
                        {
                            anyInRange = true;
                            break;
                        }
                    }

                    if (!anyInRange)
                    {
                        return new CastSkillOutcome(CombatStatus.OutOfRange, skillId, 0, 0, 0, vitals.ToView(), 0);
                    }

                    vitals.Mp -= skill.MpCost;
                    for (int i = 0; i < instance.Monsters.Count; i++)
                    {
                        MonsterSpawn m = instance.Monsters[i];
                        if (m.Hp <= 0)
                        {
                            continue;
                        }

                        int dist = Math.Max(Math.Abs(playerTileX - m.X), Math.Abs(playerTileY - m.Y));
                        if (dist <= skill.Range)
                        {
                            int effectivePower = m.Modifier == MonsterModifier.Shielded
                                ? Math.Max(1, (int)(scaledPower * 0.75f))
                                : scaledPower;
                            int dmg = Math.Min(m.Hp, effectivePower);
                            int remaining = m.Hp - dmg;
                            MonsterSpawn updated = m with { Hp = remaining };
                            instance.Monsters[i] = updated;
                            broadcastChanges.Add(updated);
                            totalDamage += dmg;
                            targetsHit++;

                            if (remaining == 0)
                            {
                                _ = MonsterCatalog.TryGet(m.Kind, out MonsterDefinition? mDef);
                                int xp = mDef?.XpReward ?? 15;
                                if (m.Modifier != MonsterModifier.None)
                                {
                                    xp = (int)(xp * 2.5);
                                }

                                vitals.CombatXp = Math.Min(SkillRules.XpCap, vitals.CombatXp + xp);
                            }
                            else
                            {
                                vitals.CombatXp = Math.Min(SkillRules.XpCap, vitals.CombatXp + SkillRules.HitXp);
                            }
                        }
                    }
                }
                else if (skill.Kind == CombatSkillKind.Fireball)
                {
                    int targetIdx = -1;
                    for (int i = 0; i < instance.Monsters.Count; i++)
                    {
                        if (instance.Monsters[i].MonsterId == targetMonsterId && instance.Monsters[i].Hp > 0)
                        {
                            targetIdx = i;
                            break;
                        }
                    }

                    if (targetIdx < 0)
                    {
                        return new CastSkillOutcome(CombatStatus.UnknownMonster, skillId, 0, 0, 0, vitals.ToView(), 0);
                    }

                    MonsterSpawn target = instance.Monsters[targetIdx];
                    int dist = Math.Max(Math.Abs(playerTileX - target.X), Math.Abs(playerTileY - target.Y));
                    if (dist > skill.Range)
                    {
                        return new CastSkillOutcome(CombatStatus.OutOfRange, skillId, 0, 0, 0, vitals.ToView(), 0);
                    }

                    vitals.Mp -= skill.MpCost;
                    int effectivePower = target.Modifier == MonsterModifier.Shielded
                        ? Math.Max(1, (int)(scaledPower * 0.75f))
                        : scaledPower;
                    int dmg = Math.Min(target.Hp, effectivePower);
                    int remaining = target.Hp - dmg;
                    MonsterSpawn updated = target with { Hp = remaining };
                    instance.Monsters[targetIdx] = updated;
                    broadcastChanges.Add(updated);
                    totalDamage += dmg;
                    targetsHit = 1;

                    if (remaining == 0)
                    {
                        _ = MonsterCatalog.TryGet(target.Kind, out MonsterDefinition? tDef);
                        int xp = tDef?.XpReward ?? 15;
                        if (target.Modifier != MonsterModifier.None)
                        {
                            xp = (int)(xp * 2.5);
                        }

                        vitals.CombatXp = Math.Min(SkillRules.XpCap, vitals.CombatXp + xp);
                    }
                    else
                    {
                        vitals.CombatXp = Math.Min(SkillRules.XpCap, vitals.CombatXp + SkillRules.HitXp);
                    }
                }

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

                resultVitals = vitals.ToView();
            }
        }

        _skillCooldowns[(player.AccountId, skillId)] = now + skill.CooldownMs;
        await this.SaveVitalsAsync(player.AccountId, resultVitals, cancellationToken).ConfigureAwait(false);

        foreach (MonsterSpawn m in broadcastChanges)
        {
            await this.BroadcastMonsterChangedAsync(player.Key, m, cancellationToken).ConfigureAwait(false);
            if (m.Hp == 0)
            {
                await this.HandleMonsterDefeatedAsync(player, player.AccountId, player.Key, player.Instance, tileSize, m, cancellationToken).ConfigureAwait(false);
            }
        }

        if (_notifier is not null && newLevel > oldLevel)
        {
            using PacketScope<LevelUp> levelLease = PacketFactory<LevelUp>.Acquire();
            levelLease.Value.Skill = (byte)Nosum.Gameplay.Skills.SkillKind.Combat;
            levelLease.Value.Level = (byte)newLevel;
            _ = await _notifier.TrySendAsync(player.AccountId, levelLease.Value, cancellationToken).ConfigureAwait(false);
        }

        byte skillCombo = 0;
        if (targetsHit > 0)
        {
            skillCombo = (byte)this.RegisterHitCombo(player.AccountId);
        }

        return new CastSkillOutcome(
            CombatStatus.Ok,
            skillId,
            totalDamage,
            targetsHit,
            healAmount,
            resultVitals,
            skill.CooldownMs,
            skillCombo);
    }

    private async Task HandleMonsterDefeatedAsync(
        PlayerPresence? killerPresence,
        Guid killerId,
        string instanceKey,
        MapInstance mapInstance,
        int tileSize,
        MonsterSpawn defeatedMonster,
        CancellationToken cancellationToken)
    {
        if (!MonsterCatalog.TryGet(defeatedMonster.Kind, out MonsterDefinition? defeatedDef))
        {
            return;
        }

        int dropPixelX = (defeatedMonster.X * tileSize) + (tileSize / 2);
        int dropPixelY = (defeatedMonster.Y * tileSize) + (tileSize / 2);
        _ = await _groundItems.DropAsync(
            new GroundDrop(mapInstance, dropPixelX, dropPixelY, defeatedDef.PrimaryDropItemId, 1, killerId),
            cancellationToken).ConfigureAwait(false);

        if (defeatedMonster.Modifier != MonsterModifier.None)
        {
            if (defeatedDef.RareDropItemId > 0)
            {
                _ = await _groundItems.DropAsync(
                    new GroundDrop(mapInstance, dropPixelX + 4, dropPixelY, defeatedDef.RareDropItemId, 1, killerId),
                    cancellationToken).ConfigureAwait(false);
            }

            _ = await _groundItems.DropAsync(
                new GroundDrop(mapInstance, dropPixelX - 4, dropPixelY + 4, ItemCatalog.TreasureGem, 1, killerId),
                cancellationToken).ConfigureAwait(false);
        }
        else if (((defeatedMonster.MonsterId + defeatedMonster.X + defeatedMonster.Y) & 1) == 0 && defeatedDef.RareDropItemId > 0)
        {
            _ = await _groundItems.DropAsync(
                new GroundDrop(mapInstance, dropPixelX + 4, dropPixelY, defeatedDef.RareDropItemId, 1, killerId),
                cancellationToken).ConfigureAwait(false);
        }

        if (killerPresence is not null)
        {
            await _groundItems.OnPlayerMovedAsync(killerPresence).ConfigureAwait(false);
        }

        // Co-op party proximity Combat XP distribution (docs/adr/ADR-052)
        List<PlayerPresence> coPlayers = [];
        _presence.CopyPlayers(instanceKey, coPlayers);
        int baseReward = defeatedMonster.Modifier != MonsterModifier.None
            ? (int)(defeatedDef.XpReward * 2.5)
            : defeatedDef.XpReward;
        int partyXp = baseReward / 2;
        if (partyXp <= 0)
        {
            return;
        }

        CoopMembershipInfo? killerCoop = _coops is not null
            ? await _coops.FindAsync(killerId, cancellationToken).ConfigureAwait(false)
            : null;
        if (_coops is not null && killerCoop is null)
        {
            return;
        }

        for (int pIdx = 0; pIdx < coPlayers.Count; pIdx++)
        {
            PlayerPresence ally = coPlayers[pIdx];
            if (ally.AccountId == killerId)
            {
                continue;
            }

            double ax;
            double ay;
            lock (ally.Gate)
            {
                ax = ally.Motion.X;
                ay = ally.Motion.Y;
            }

            int allyTileX = (int)Math.Floor(ax / tileSize);
            int allyTileY = (int)Math.Floor(ay / tileSize);
            int allyDist = Math.Max(Math.Abs(allyTileX - defeatedMonster.X), Math.Abs(allyTileY - defeatedMonster.Y));
            if (allyDist > CombatRules.CoopShareRadius)
            {
                continue;
            }

            if (_coops is not null)
            {
                CoopMembershipInfo? allyCoop = await _coops.FindAsync(ally.AccountId, cancellationToken).ConfigureAwait(false);
                if (allyCoop is null || allyCoop.CoopId != killerCoop!.CoopId)
                {
                    continue;
                }
            }

            PlayerVitalsState allyVitals = await this.GetOrCreateVitalsAsync(ally.AccountId, cancellationToken).ConfigureAwait(false);
            VitalsView allyView;
            int oldAllyLevel;
            int newAllyLevel;
            lock (allyVitals.Gate)
            {
                oldAllyLevel = SkillRules.LevelOf(allyVitals.CombatXp);
                allyVitals.CombatXp = Math.Min(SkillRules.XpCap, allyVitals.CombatXp + partyXp);
                newAllyLevel = SkillRules.LevelOf(allyVitals.CombatXp);
                if (newAllyLevel > oldAllyLevel)
                {
                    int allyMaxHp = CombatRules.MaxHpForLevel(newAllyLevel);
                    int allyMaxMp = CombatRules.MaxMpForLevel(newAllyLevel);
                    int hpBonus = Math.Max(0, allyMaxHp - allyVitals.MaxHp);
                    int mpBonus = Math.Max(0, allyMaxMp - allyVitals.MaxMp);
                    allyVitals.MaxHp = allyMaxHp;
                    allyVitals.MaxMp = allyMaxMp;
                    allyVitals.Hp = Math.Min(allyMaxHp, allyVitals.Hp + hpBonus);
                    allyVitals.Mp = Math.Min(allyMaxMp, allyVitals.Mp + mpBonus);
                }

                allyView = allyVitals.ToView();
            }

            await this.SaveVitalsAsync(ally.AccountId, allyView, cancellationToken).ConfigureAwait(false);

            if (_notifier is not null)
            {
                using PacketScope<VitalsState> lease = PacketFactory<VitalsState>.Acquire();
                lease.Value.Hp = (ushort)allyView.Hp;
                lease.Value.MaxHp = (ushort)allyView.MaxHp;
                lease.Value.Mp = (ushort)allyView.Mp;
                lease.Value.MaxMp = (ushort)allyView.MaxMp;
                lease.Value.CombatXp = allyView.CombatXp;
                lease.Value.StatusEffect = allyView.StatusEffect;
                _ = await _notifier.TrySendAsync(ally.AccountId, lease.Value, cancellationToken).ConfigureAwait(false);

                if (newAllyLevel > oldAllyLevel)
                {
                    using PacketScope<LevelUp> levelLease = PacketFactory<LevelUp>.Acquire();
                    levelLease.Value.Skill = (byte)SkillKind.Combat;
                    levelLease.Value.Level = (byte)newAllyLevel;
                    _ = await _notifier.TrySendAsync(ally.AccountId, levelLease.Value, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async ValueTask<PlayerVitalsState> GetOrCreateVitalsAsync(Guid playerId, CancellationToken cancellationToken)
    {
        if (_vitals.TryGetValue(playerId, out PlayerVitalsState? existing))
        {
            return existing;
        }

        PlayerStateCache.Snapshot? loaded = null;
        if (_stateCache is not null && _stateCache.TryPeek(playerId, out PlayerStateCache.Snapshot peeked) && peeked.Exists)
        {
            loaded = peeked;
        }
        else if (_unitOfWork is not null)
        {
            loaded = _stateCache is not null
                ? await _stateCache.GetAsync(_unitOfWork, playerId, cancellationToken).ConfigureAwait(false)
                : await PlayerStateCache.ReadRowAsync(_unitOfWork, playerId, cancellationToken).ConfigureAwait(false);
        }

        return _vitals.GetOrAdd(playerId, _ => CreateVitalsFromSnapshot(loaded));
    }

    private static PlayerVitalsState CreateVitalsFromSnapshot(PlayerStateCache.Snapshot? snapshot)
    {
        if (snapshot is not { Exists: true } s)
        {
            return new PlayerVitalsState();
        }

        int xp = Math.Clamp(s.CombatXp, 0, SkillRules.XpCap);
        int level = SkillRules.LevelOf(xp);
        int maxHp = CombatRules.MaxHpForLevel(level);
        int maxMp = CombatRules.MaxMpForLevel(level);
        int hp = s.Hp > 0 ? Math.Clamp(s.Hp, 1, maxHp) : maxHp;
        int mp = Math.Clamp(s.Mp, 0, maxMp);
        return new PlayerVitalsState
        {
            CombatXp = xp,
            MaxHp = maxHp,
            MaxMp = maxMp,
            Hp = hp,
            Mp = mp,
        };
    }

    [SuppressMessage("Design", "CA1031", Justification = "Vitals persistence is best-effort alongside in-memory authoritative state.")]
    private async ValueTask SaveVitalsAsync(Guid playerId, VitalsView view, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _stateCache?.Apply(playerId, s => s with { CombatXp = view.CombatXp, Hp = view.Hp, Mp = view.Mp, UpdatedAt = now });

        if (_unitOfWork is null)
        {
            return;
        }

        try
        {
            StripedAsyncLock.Releaser gate = _stateCache is not null
                ? await _stateCache.Locks.EnterAsync(playerId, cancellationToken).ConfigureAwait(false)
                : default;
            try
            {
                IUnitOfWork unit = _unitOfWork.Create();
                await using (unit.ConfigureAwait(false))
                {
                    IRepository<PlayerState> repo = unit.Repository<PlayerState>();
                    PlayerState? row = await repo.FindAsync(playerId, cancellationToken).ConfigureAwait(false);
                    if (row is null)
                    {
                        repo.Add(new PlayerState
                        {
                            PlayerId = playerId,
                            MapName = string.Empty,
                            Energy = EnergyRules.MaxEnergy,
                            EnergyRefilledDay = -1,
                            CombatXp = view.CombatXp,
                            Hp = view.Hp,
                            Mp = view.Mp,
                            UpdatedAt = now,
                        });
                    }
                    else
                    {
                        row.CombatXp = view.CombatXp;
                        row.Hp = view.Hp;
                        row.Mp = view.Mp;
                        row.UpdatedAt = now;
                        repo.Update(
                            row,
                            nameof(PlayerState.CombatXp),
                            nameof(PlayerState.Hp),
                            nameof(PlayerState.Mp),
                            nameof(PlayerState.UpdatedAt));
                    }

                    _ = await unit.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                _stateCache?.Apply(playerId, s => s with { Exists = true, CombatXp = view.CombatXp, Hp = view.Hp, Mp = view.Mp, UpdatedAt = now });
            }
            finally
            {
                if (_stateCache is not null)
                {
                    gate.Dispose();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _stateCache?.Forget(playerId);
        }
    }

    private InstanceMonsters GetOrCreateInstance(PlayerPresence player)
    {
        long today = _dayClock.Today;
        string key = $"{player.Key}:{today}";
        return _instances.GetOrAdd(key, k =>
        {
            if (!_maps.TryGet(player.MapId, out MapDefinition? map))
            {
                return new InstanceMonsters([], []);
            }

            int floor = GameMaps.TryGet(player.MapId, out MapSpec? spec) ? spec.MineFloor : 0;
            ulong seed = floor >= 1
                ? MineRules.FloorSeed(player.AccountId, today, floor)
                : NodeRandom.Mix(NodeRandom.HashOf(player.MapId), today, 0x4D4F4E);

            HashSet<(int X, int Y)> occupied = [];
            foreach ((int rx, int ry) in map.ReservedTiles)
            {
                _ = occupied.Add((rx, ry));
            }

            int tileSize = Math.Max(1, map.Grid.TileSize);
            int spawnX = (int)(map.SpawnX / tileSize);
            int spawnY = (int)(map.SpawnY / tileSize);
            if (floor >= 1)
            {
                MineFloorPlanner.Plan nodePlan = MineFloorPlanner.Build(floor, seed, map.Grid, map.ReservedTiles, spawnX, spawnY);
                foreach (Nosum.Gameplay.Nodes.NodePlacement node in nodePlan.Nodes)
                {
                    _ = occupied.Add((node.X, node.Y));
                }
            }
            else
            {
                foreach (NodeSite site in map.NodeSites)
                {
                    _ = occupied.Add((site.X, site.Y));
                }
            }

            ExcludeUnreachableTiles(map.Grid, spawnX, spawnY, occupied);

            List<MonsterSpawn> planned = CombatRules.PlanMonsters(player.MapId, seed, map.Grid, occupied, spawnX, spawnY);
            foreach (MonsterSpawn m in planned)
            {
                _ = occupied.Add((m.X, m.Y));
            }

            List<DungeonHazard> hazards = CombatRules.PlanHazards(player.MapId, seed, map.Grid, spawnX, spawnY, occupied);
            foreach (DungeonHazard h in hazards)
            {
                _ = occupied.Add((h.X, h.Y));
            }

            List<DungeonHazard> shrines = CombatRules.PlanShrines(player.MapId, seed, map.Grid, spawnX, spawnY, occupied);
            hazards.AddRange(shrines);
            return new InstanceMonsters(planned, hazards);
        });
    }

    private static void ExcludeUnreachableTiles(Nosum.Gameplay.Movement.WalkGrid grid, int spawnX, int spawnY, HashSet<(int X, int Y)> occupied)
    {
        if (grid.Width <= 0 || grid.Height <= 0 || grid.IsBlocked(spawnX, spawnY))
        {
            return;
        }

        bool[,] reachable = new bool[grid.Width, grid.Height];
        Queue<(int X, int Y)> queue = new();
        reachable[spawnX, spawnY] = true;
        queue.Enqueue((spawnX, spawnY));

        ReadOnlySpan<(int Dx, int Dy)> dirs = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (queue.Count > 0)
        {
            (int cx, int cy) = queue.Dequeue();
            foreach ((int dx, int dy) in dirs)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if ((uint)nx < (uint)grid.Width && (uint)ny < (uint)grid.Height && !reachable[nx, ny] && !grid.IsBlocked(nx, ny))
                {
                    reachable[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (!grid.IsBlocked(x, y) && !reachable[x, y])
                {
                    _ = occupied.Add((x, y));
                }
            }
        }
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
            lease.Value.Modifier = (byte)monster.Modifier;
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
            string prefix = player.Key + ":";
            foreach (string key in _instances.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    _ = _instances.TryRemove(key, out _);
                }
            }
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

        public StatusEffectKind ActiveEffect { get; set; }

        public int EffectRemainingTicks { get; set; }

        public int EffectMagnitude { get; set; }

        public VitalsView ToView() => new(Hp, MaxHp, Mp, MaxMp, CombatXp, (byte)ActiveEffect);
    }

    private sealed class InstanceMonsters
    {
        public InstanceMonsters(List<MonsterSpawn> monsters, List<DungeonHazard> hazards)
        {
            Monsters = monsters;
            Hazards = hazards;
        }

        public object Gate { get; } = new();

        public List<MonsterSpawn> Monsters { get; }

        public List<DungeonHazard> Hazards { get; }
    }
}
