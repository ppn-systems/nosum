// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Player;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Combat.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Looks;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Skills;

namespace Nosum.Infrastructure.Combat;

public sealed partial class CombatService
{
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
        if (_presence.TryGetPlayer(accountId, out PlayerPresence? direct))
        {
            return direct;
        }

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
}
