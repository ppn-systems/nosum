// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Abstractions.Networking;
using Nalix.Codec.Pooling;
using Nosum.Abstractions.Combat;
using Nosum.Abstractions.Coop;
using Nosum.Abstractions.GroundItems;
using Nosum.Abstractions.Maps;
using Nosum.Abstractions.Presence;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.S2C;
using Nosum.Contracts.Player.S2C;
using Nosum.Gameplay.Combat;
using Nosum.Gameplay.Items;
using Nosum.Gameplay.Maps;
using Nosum.Gameplay.Skills;

namespace Nosum.Infrastructure.Combat;

public sealed partial class CombatService
{
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
            levelLease.Value.Skill = (byte)SkillKind.Combat;
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

        // Every kill counts into the hunting log (docs/adr/ADR-067).
        if (_bestiary is not null)
        {
            await _bestiary.RecordKillAsync(killerId, defeatedMonster.Kind, cancellationToken).ConfigureAwait(false);
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
}
