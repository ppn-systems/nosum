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
        List<(int X, int Y, int MonsterId, MonsterKind Kind)> slams = new(2);
        List<PendingContact> contacts = new(4);
        List<PendingHazard> hazardHits = new(4);
        List<PendingShrine> shrineHits = new(2);

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
                    slams.Clear();
                    contacts.Clear();
                    hazardHits.Clear();
                    shrineHits.Clear();
                    long slamNow = Environment.TickCount64;
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
                                bool enraged = CombatRules.IsEnraged(m);
                                if (CombatRules.IsBoss(m.Kind) && (!instance.SlamReadyAt.TryGetValue(m.MonsterId, out long slamReadyAt) || slamNow >= slamReadyAt))
                                {
                                    instance.SlamReadyAt[m.MonsterId] = slamNow + CombatRules.GroundSlamTelegraphMs + CombatRules.GroundSlamCooldownMs;
                                    slams.Add((targetTileX, targetTileY, m.MonsterId, m.Kind));
                                }

                                if (bestDist <= 1)
                                {
                                    // Strikes are collected under the gate and awaited after it is released (issue #149).
                                    contacts.Add(new PendingContact(nearest, CombatRules.MonsterDamage(m.Kind), m.Kind, m.MonsterId));
                                }
                                else if (MonsterStepRules.TryStepToward(map.Grid, instance.Monsters, m.MonsterId, m.X, m.Y, targetTileX, targetTileY, out int stepX, out int stepY))
                                {
                                    // No corner clipping and no stacking on another monster (issue #166).
                                    MonsterSpawn updated = m with { X = stepX, Y = stepY };
                                    if (m.Modifier == MonsterModifier.Swift || enraged)
                                    {
                                        int distAfterFirstStep = Math.Max(Math.Abs(targetTileX - updated.X), Math.Abs(targetTileY - updated.Y));
                                        if (distAfterFirstStep > 1
                                            && MonsterStepRules.TryStepToward(map.Grid, instance.Monsters, m.MonsterId, updated.X, updated.Y, targetTileX, targetTileY, out int step2X, out int step2Y))
                                        {
                                            updated = updated with { X = step2X, Y = step2Y };
                                        }
                                    }

                                    instance.Monsters[i] = updated;
                                    moved.Add(updated);
                                }
                            }
                            else if (Random.Shared.Next(4) == 0)
                            {
                                int dir = Random.Shared.Next(4);
                                int wx = m.X + (dir == 0 ? 1 : dir == 1 ? -1 : 0);
                                int wy = m.Y + (dir == 2 ? 1 : dir == 3 ? -1 : 0);
                                if (MonsterStepRules.CanStep(map.Grid, m.X, m.Y, wx, wy) && !MonsterStepRules.IsOccupied(instance.Monsters, m.MonsterId, wx, wy))
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
                                bool isActive = h.Kind != DungeonHazardKind.SpikeTrap || DungeonHazardRules.IsSpikeTrapActive(h.HazardId, now);

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
                                    if (pTileX != h.X || pTileY != h.Y)
                                    {
                                        continue;
                                    }

                                    if (CombatRules.IsShrine(h.Kind))
                                    {
                                        shrineHits.Add(new PendingShrine(p.AccountId, h, now));
                                    }
                                    else if (this.TryClaimHazardContact(p.AccountId, instanceKey, h, now))
                                    {
                                        hazardHits.Add(new PendingHazard(p, h.Damage, h.Kind));
                                    }
                                }
                            }
                        }
                    }

                    for (int mIdx = 0; mIdx < moved.Count; mIdx++)
                    {
                        await this.BroadcastMonsterChangedAsync(instanceKey, moved[mIdx], cancellationToken).ConfigureAwait(false);
                    }

                    for (int cIdx = 0; cIdx < contacts.Count; cIdx++)
                    {
                        PendingContact c = contacts[cIdx];
                        await this.ApplyMonsterContactAttackAsync(c.Player, c.Damage, c.Kind, instanceKey, instance, c.MonsterId, cancellationToken).ConfigureAwait(false);
                    }

                    for (int hIdx = 0; hIdx < hazardHits.Count; hIdx++)
                    {
                        PendingHazard hit = hazardHits[hIdx];
                        await this.ApplyHazardContactAsync(hit.Player, hit.Damage, hit.Kind, cancellationToken).ConfigureAwait(false);
                    }

                    for (int shIdx = 0; shIdx < shrineHits.Count; shIdx++)
                    {
                        PendingShrine shrine = shrineHits[shIdx];
                        await this.ApplyShrineBlessingAsync(shrine.PlayerId, instanceKey, instance, shrine.Shrine, shrine.Now, cancellationToken).ConfigureAwait(false);
                    }

                    for (int sIdx = 0; sIdx < slams.Count; sIdx++)
                    {
                        (int slamX, int slamY, int slamMonsterId, MonsterKind slamKind) = slams[sIdx];
                        await this.BroadcastGroundTelegraphAsync(instanceKey, slamX, slamY, cancellationToken).ConfigureAwait(false);
                        // Detached on purpose: the telegraph delay outlives the tick. Its failures are reported, never dropped (issue #149).
                        _ = this.ResolveGroundSlamAsync(instanceKey, instance, slamX, slamY, slamMonsterId, slamKind, onError, cancellationToken);
                    }
                }

                foreach (KeyValuePair<Guid, PlayerVitalsState> kvp in _vitals)
                {
                    PlayerVitalsState state = kvp.Value;
                    bool changed = false;
                    bool knockedOut = false;
                    bool buffExpired = false;
                    VitalsView view;
                    lock (state.Gate)
                    {
                        if (state.ActiveBuff != StatusEffectKind.None && Environment.TickCount64 >= state.BuffExpiresAt)
                        {
                            state.ActiveBuff = StatusEffectKind.None;
                            buffExpired = true;
                            changed = true;
                        }

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
                                    state.ActiveBuff = StatusEffectKind.None;
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

                    if (buffExpired)
                    {
                        this.SyncMoveSpeed(kvp.Key, StatusEffectKind.None);
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
                            lease.Value.Buff = view.Buff;
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

    /// <summary>
    /// Waits out the telegraph, then strikes every player still inside the slam radius around the telegraphed tile (docs/adr/ADR-047).
    /// </summary>
    [SuppressMessage("Design", "CA1031", Justification = "A detached slam must report every failure to the AI error callback instead of leaking it unobserved.")]
    private async Task ResolveGroundSlamAsync(
        string instanceKey,
        InstanceMonsters instance,
        int centerX,
        int centerY,
        int monsterId,
        MonsterKind kind,
        Action<Exception>? onError,
        CancellationToken cancellationToken)
    {
        try
        {
            await this.ResolveGroundSlamCoreAsync(instanceKey, instance, centerX, centerY, monsterId, kind, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
    }

    private async Task ResolveGroundSlamCoreAsync(
        string instanceKey,
        InstanceMonsters instance,
        int centerX,
        int centerY,
        int monsterId,
        MonsterKind kind,
        CancellationToken cancellationToken)
    {
        await Task.Delay(CombatRules.GroundSlamTelegraphMs, cancellationToken).ConfigureAwait(false);

        List<PlayerPresence> present = [];
        _presence.CopyPlayers(instanceKey, present);
        if (present.Count == 0 || !_maps.TryGet(present[0].MapId, out MapDefinition? map))
        {
            return;
        }

        int tileSize = Math.Max(1, map.Grid.TileSize);
        int damage = CombatRules.GroundSlamDamage(kind);
        for (int i = 0; i < present.Count; i++)
        {
            PlayerPresence p = present[i];
            double px;
            double py;
            lock (p.Gate)
            {
                px = p.Motion.X;
                py = p.Motion.Y;
            }

            int pTileX = (int)Math.Floor(px / tileSize);
            int pTileY = (int)Math.Floor(py / tileSize);
            if (Math.Max(Math.Abs(pTileX - centerX), Math.Abs(pTileY - centerY)) > CombatRules.GroundSlamRadiusTiles)
            {
                continue;
            }

            await this.ApplyMonsterContactAttackAsync(p, damage, kind, instanceKey, instance, monsterId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask BroadcastGroundTelegraphAsync(string instanceKey, int centerX, int centerY, CancellationToken cancellationToken)
    {
        List<PlayerPresence> present = [];
        _presence.CopyPlayers(instanceKey, present);
        if (present.Count == 0 || _notifier is null)
        {
            return;
        }

        foreach (PlayerPresence p in present)
        {
            using PacketScope<GroundTelegraph> lease = PacketFactory<GroundTelegraph>.Acquire();
            lease.Value.X = (ushort)centerX;
            lease.Value.Y = (ushort)centerY;
            lease.Value.RadiusTiles = (byte)CombatRules.GroundSlamRadiusTiles;
            lease.Value.DelayMs = (ushort)CombatRules.GroundSlamTelegraphMs;
            _ = await _notifier.TrySendAsync(p.AccountId, lease.Value, cancellationToken).ConfigureAwait(false);
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
        this.SyncMoveSpeed(playerId, StatusEffectKind.None);

        if (_notifier is not null)
        {
            using PacketScope<VitalsState> zeroLease = PacketFactory<VitalsState>.Acquire();
            zeroLease.Value.Hp = 0;
            zeroLease.Value.MaxHp = (ushort)revivedView.MaxHp;
            zeroLease.Value.Mp = (ushort)revivedView.Mp;
            zeroLease.Value.MaxMp = (ushort)revivedView.MaxMp;
            zeroLease.Value.CombatXp = revivedView.CombatXp;
            zeroLease.Value.StatusEffect = 0;
            zeroLease.Value.Buff = 0;
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
            lease.Value.Buff = revivedView.Buff;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
    }
}
