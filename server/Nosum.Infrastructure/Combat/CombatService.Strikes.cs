// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
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

/// <summary>Monster contact strikes, hazard and shrine contact, and hazards under a player's feet after a move (docs/adr/ADR-061, ADR-063).</summary>
public sealed partial class CombatService
{
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
                state.ActiveBuff = StatusEffectKind.None;
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
            lease.Value.Buff = updated.Buff;
            _ = await _notifier.TrySendAsync(playerId, lease.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask OnPlayerMovedAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!_presence.TryGet(connection, out PlayerPresence? player) || !_maps.TryGet(player.MapId, out MapDefinition? map))
        {
            return;
        }

        int tileSize = Math.Max(1, map.Grid.TileSize);
        double feetX;
        double feetY;
        lock (player.Gate)
        {
            feetX = player.Motion.X;
            feetY = player.Motion.Y;
        }

        int tileX = (int)Math.Floor(feetX / tileSize);
        int tileY = (int)Math.Floor(feetY / tileSize);
        // A map with no combat instance has no hazards: never plan one just for a move.
        if (!_instances.TryGetValue($"{player.Key}:{_dayClock.Today}", out InstanceMonsters? instance))
        {
            return;
        }

        long now = Environment.TickCount64;
        List<PendingHazard>? hits = null;
        lock (instance.Gate)
        {
            for (int i = 0; i < instance.Hazards.Count; i++)
            {
                DungeonHazard h = instance.Hazards[i];
                if (h.X != tileX || h.Y != tileY || CombatRules.IsShrine(h.Kind))
                {
                    continue;
                }

                if (h.Kind == DungeonHazardKind.SpikeTrap && !DungeonHazardRules.IsSpikeTrapActive(h.HazardId, now))
                {
                    continue;
                }

                if (this.TryClaimHazardContact(player.AccountId, player.Key, h, now))
                {
                    hits ??= [];
                    hits.Add(new PendingHazard(player, h.Damage, h.Kind));
                }
            }
        }

        if (hits is null)
        {
            return;
        }

        for (int i = 0; i < hits.Count; i++)
        {
            PendingHazard hit = hits[i];
            await this.ApplyHazardContactAsync(hit.Player, hit.Damage, hit.Kind, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Claims one contact hit from a hazard for a player: a hazard hits again only after its cooldown, so standing on or
    /// moving over it never hits on every packet (issue #168). Callers hold the instance gate.
    /// </summary>
    private bool TryClaimHazardContact(Guid playerId, string instanceKey, DungeonHazard hazard, long now)
    {
        (Guid PlayerId, string InstanceKey, int HazardId) key = (playerId, instanceKey, hazard.HazardId);
        if (_hazardContacts.TryGetValue(key, out long lastAt) && now - lastAt < DungeonHazardRules.ContactCooldownMs(hazard.Kind))
        {
            return false;
        }

        _hazardContacts[key] = now;
        return true;
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
                state.ActiveBuff = StatusEffectKind.None;
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
            lease.Value.Buff = updated.Buff;
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

            if (StatusEffectRules.IsBuff(buff))
            {
                ApplyBuffLocked(state, buff, StatusEffectRules.ShrineBuffSeconds);
            }
            else if (buff != StatusEffectKind.None)
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
        this.SyncMoveSpeed(playerId, updated.Buff);

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
            lease.Value.Buff = updated.Buff;
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
