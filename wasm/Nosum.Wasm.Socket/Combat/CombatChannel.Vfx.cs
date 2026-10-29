// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Combat.S2C;
using Nosum.Gameplay.Skills;

namespace Nosum.Wasm.Socket.Combat;

/// <summary>
/// Visual effects, floating numbers, projectiles, death effects, and combo/level-up celebration logic for <see cref="CombatChannel"/>.
/// </summary>
public sealed partial class CombatChannel
{
    private const int MaxGroundTelegraphs = 8;

    /// <summary>Gets whether the currently firing <see cref="LevelledUp"/> event originated from Combat XP rather than an external skill.</summary>
    public bool IsCombatSkillLevelUp { get; private set; }

    /// <summary>Immediately activates the client-side visual animation for a cast skill.</summary>
    /// <param name="skillId">Skill id (1 = Whirlwind, 2 = Fireball, 3 = DivineHeal).</param>
    /// <param name="targetMonsterId">Optional target monster id for Fireball trajectory.</param>
    public void TriggerSkillEffect(byte skillId, int targetMonsterId = 0)
    {
        Volatile.Write(ref _activeSkillId, skillId);
        Volatile.Write(ref _activeSkillTick, Environment.TickCount64);

        if (skillId == 2)
        {
            if (targetMonsterId != 0 && this.TryGetMonster(targetMonsterId, out MonsterClientRow tm))
            {
                this.SpawnProjectile(_playerX, _playerY, tm.X, tm.Y, 2, 320f);
            }
            else if (this.TryFindTarget(_playerX, _playerY, 6, out MonsterClientRow near))
            {
                this.SpawnProjectile(_playerX, _playerY, near.X, near.Y, 2, 320f);
            }
            else
            {
                (int dx, int dy) = (_playerFacing & 3) switch
                {
                    1 => (5, 0),
                    2 => (0, -5),
                    3 => (-5, 0),
                    _ => (0, 5),
                };
                this.SpawnProjectile(_playerX, _playerY, _playerX + dx, _playerY + dy, 2, 320f);
            }
        }
    }

    /// <summary>Queries the currently active skill animation for zero-allocation world rendering.</summary>
    public bool TryGetActiveSkillEffect(long now, out byte skillId, out float progress, out int playerX, out int playerY)
    {
        long start = Volatile.Read(ref _activeSkillTick);
        int id = Volatile.Read(ref _activeSkillId);
        long elapsed = now - start;
        if (id > 0 && elapsed >= 0 && elapsed < 550)
        {
            skillId = (byte)id;
            progress = elapsed / 550f;
            playerX = _playerX;
            playerY = _playerY;
            return true;
        }

        skillId = 0;
        progress = 0f;
        playerX = 0;
        playerY = 0;
        return false;
    }

    /// <summary>Spawns a client-side magic projectile along a trajectory (docs/adr/ADR-048).</summary>
    /// <param name="fromX">Starting tile X.</param>
    /// <param name="fromY">Starting tile Y.</param>
    /// <param name="toX">Target tile X.</param>
    /// <param name="toY">Target tile Y.</param>
    /// <param name="kind">Projectile visual kind.</param>
    /// <param name="durationMs">Travel duration in ms.</param>
    public void SpawnProjectile(float fromX, float fromY, float toX, float toY, byte kind = 1, float durationMs = 260f)
    {
        lock (_gate)
        {
            if (_projectiles.Count >= 32)
            {
                _projectiles.RemoveAt(0);
            }

            _projectiles.Add(new ProjectileClientRow(fromX, fromY, toX, toY, Environment.TickCount64, durationMs, kind));
        }
    }

    /// <summary>Copies currently active or impacting projectiles without heap allocation.</summary>
    /// <param name="destination">Destination list to populate.</param>
    /// <param name="now">Current tick timestamp.</param>
    public void CopyProjectiles(List<ProjectileClientRow> destination, long now)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        lock (_gate)
        {
            for (int i = _projectiles.Count - 1; i >= 0; i--)
            {
                ProjectileClientRow p = _projectiles[i];
                if (now - p.StartTime > p.DurationMs + 200)
                {
                    _projectiles.RemoveAt(i);
                }
                else
                {
                    destination.Add(p);
                }
            }
        }
    }

    /// <summary>Copies active monster death poofs without heap allocation.</summary>
    /// <param name="destination">Destination list to populate.</param>
    /// <param name="now">Current tick timestamp.</param>
    public void CopyDeathEffects(List<DeathEffectClientRow> destination, long now)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();

        lock (_gate)
        {
            for (int i = _deathEffects.Count - 1; i >= 0; i--)
            {
                DeathEffectClientRow effect = _deathEffects[i];
                if ((now - effect.StartTime) >= effect.DurationMs)
                {
                    _deathEffects.RemoveAt(i);
                }
                else
                {
                    destination.Add(effect);
                }
            }
        }
    }

    /// <summary>Updates local player coordinates and facing for floating combat text and skill animations.</summary>
    public void SetLocalPosition(int tileX, int tileY, byte facing = 0)
    {
        _playerX = tileX;
        _playerY = tileY;
        _playerFacing = facing;
    }

    /// <summary>Spawns a floating combat number in world pixels (docs/adr/ADR-055).</summary>
    public void SpawnFloatingText(int worldX, int worldY, int amount, byte style = 0, float durationMs = 650f)
    {
        if (amount <= 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_floatingTexts.Count >= 32)
            {
                _floatingTexts.RemoveAt(0);
            }

            _floatingTexts.Add(new FloatingTextClientRow(worldX, worldY, amount, style, Environment.TickCount64, durationMs));
        }
    }

    /// <summary>Copies active floating combat numbers without allocation.</summary>
    public void CopyFloatingTexts(List<FloatingTextClientRow> destination, long now)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();

        lock (_gate)
        {
            for (int i = _floatingTexts.Count - 1; i >= 0; i--)
            {
                FloatingTextClientRow f = _floatingTexts[i];
                if ((now - f.StartTime) >= f.DurationMs)
                {
                    _floatingTexts.RemoveAt(i);
                }
                else
                {
                    destination.Add(f);
                }
            }
        }
    }

    /// <summary>Triggers the celebratory level-up radiance beacon and banner (docs/adr/ADR-062).</summary>
    /// <param name="level">New level reached.</param>
    public void TriggerLevelUp(int level)
    {
        if (level <= 0)
        {
            return;
        }

        Volatile.Write(ref _levelUpValue, level);
        Volatile.Write(ref _levelUpTick, Environment.TickCount64);
        this.LevelledUp?.Invoke(level);
        this.Changed?.Invoke();
        _ = this.ExpireLevelUpBannerAsync();
    }

    /// <summary>Queries the active level-up celebration progress for zero-allocation world rendering (docs/adr/ADR-062).</summary>
    public bool TryGetActiveLevelUp(long now, out float progress, out int level, out int playerX, out int playerY)
    {
        long start = Volatile.Read(ref _levelUpTick);
        int lvl = Volatile.Read(ref _levelUpValue);
        long elapsed = now - start;
        if (lvl > 0 && elapsed >= 0 && elapsed < 3800)
        {
            progress = elapsed / 3800f;
            level = lvl;
            playerX = _playerX;
            playerY = _playerY;
            return true;
        }

        progress = 0f;
        level = 0;
        playerX = 0;
        playerY = 0;
        return false;
    }

    private void CheckCombatLevelUp(int newCombatXp)
    {
        int newLevel = SkillRules.LevelOf(newCombatXp);
        if (!_hasInitialVitals)
        {
            _hasInitialVitals = true;
            _lastCelebratedLevel = newLevel;
            return;
        }

        if (newLevel > _lastCelebratedLevel)
        {
            _lastCelebratedLevel = newLevel;
            this.IsCombatSkillLevelUp = true;
            try
            {
                this.TriggerLevelUp(newLevel);
            }
            finally
            {
                this.IsCombatSkillLevelUp = false;
            }
        }
    }

    private void RecordCombo(byte comboCount)
    {
        long expireAt = Environment.TickCount64 + 3500;
        Volatile.Write(ref _comboCount, comboCount);
        Volatile.Write(ref _comboExpireTick, expireAt);
        this.ComboChanged?.Invoke();
        _ = this.ExpireComboAsync(expireAt);
    }

    private async Task ExpireComboAsync(long expireAt)
    {
        await Task.Delay(3550).ConfigureAwait(false);
        if (Volatile.Read(ref _comboExpireTick) == expireAt && Volatile.Read(ref _comboCount) > 0)
        {
            Volatile.Write(ref _comboCount, 0);
            this.ComboChanged?.Invoke();
            this.Changed?.Invoke();
        }
    }

    private async Task ExpireSkillCooldownAsync(byte skillId, int cooldownMs, long readyAt)
    {
        _ = cooldownMs;
        while (true)
        {
            long remainingMs = readyAt - Environment.TickCount64;
            if (remainingMs <= 0)
            {
                break;
            }

            int delayMs = (int)Math.Min(remainingMs + 25, 500);
            await Task.Delay(delayMs).ConfigureAwait(false);
            bool stillActive = false;
            lock (_gate)
            {
                if (_skillReadyAt.TryGetValue(skillId, out long current) && current == readyAt)
                {
                    stillActive = true;
                }
            }

            if (!stillActive)
            {
                return;
            }

            this.Changed?.Invoke();
        }

        bool notify = false;
        lock (_gate)
        {
            if (_skillReadyAt.TryGetValue(skillId, out long current) && current == readyAt)
            {
                notify = true;
            }
        }

        if (notify)
        {
            this.Changed?.Invoke();
        }
    }

    private async Task ExpireLevelUpBannerAsync()
    {
        await Task.Delay(3900).ConfigureAwait(false);
        this.Changed?.Invoke();
    }

    private void OnVitals(VitalsState packet)
    {
        if (packet.MaxHp <= 0)
        {
            return;
        }

        bool hadInitialVitals = _hasInitialVitals;
        int oldHp = Volatile.Read(ref _hp);
        int oldMp = Volatile.Read(ref _mp);
        int diff = packet.Hp - oldHp;
        int mpDiff = packet.Mp - oldMp;
        if (hadInitialVitals && oldHp > 0 && diff != 0)
        {
            if (diff > 0)
            {
                this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 6, diff, 3);
            }
            else
            {
                byte style = (byte)(packet.StatusEffect == 2 ? 4 : 2);
                this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 6, -diff, style);
            }
        }
        else if (hadInitialVitals && oldMp > 0 && mpDiff > 0)
        {
            this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 6, mpDiff, 3);
        }

        Volatile.Write(ref _hp, packet.Hp);
        Volatile.Write(ref _maxHp, packet.MaxHp);
        Volatile.Write(ref _mp, packet.Mp);
        Volatile.Write(ref _maxMp, packet.MaxMp);
        Volatile.Write(ref _combatXp, packet.CombatXp);
        Volatile.Write(ref _statusEffect, (int)packet.StatusEffect);
        if (packet.Hp == 0)
        {
            this.KnockedOut?.Invoke();
        }

        this.CheckCombatLevelUp(packet.CombatXp);
        this.Changed?.Invoke();
    }

    private void OnMonsterChanged(MonsterChanged packet)
    {
        lock (_gate)
        {
            bool found = false;
            for (int i = 0; i < _monsters.Count; i++)
            {
                if (_monsters[i].MonsterId == packet.MonsterId)
                {
                    if (packet.Hp == 0)
                    {
                        MonsterClientRow old = _monsters[i];
                        byte poofKind = (byte)(old.Kind >= 9 ? 2 : (old.Kind == 3 ? 2 : 0));
                        _deathEffects.Add(new DeathEffectClientRow(old.X, old.Y, Environment.TickCount64, 400f, poofKind));
                        _monsters.RemoveAt(i);
                    }
                    else
                    {
                        _monsters[i] = new MonsterClientRow(packet.MonsterId, packet.Kind, packet.X, packet.Y, packet.Hp, packet.MaxHp, packet.Modifier, packet.Enraged != 0);
                    }

                    found = true;
                    break;
                }
            }

            if (!found && packet.Hp > 0)
            {
                _monsters.Add(new MonsterClientRow(packet.MonsterId, packet.Kind, packet.X, packet.Y, packet.Hp, packet.MaxHp, packet.Modifier, packet.Enraged != 0));
            }
        }

        this.MonstersUpdated?.Invoke();
    }

    private void OnGroundTelegraph(GroundTelegraph packet)
    {
        long now = Environment.TickCount64;
        lock (_gate)
        {
            for (int i = _telegraphs.Count - 1; i >= 0; i--)
            {
                if (now - _telegraphs[i].StartMs >= _telegraphs[i].DelayMs)
                {
                    _telegraphs.RemoveAt(i);
                }
            }

            if (_telegraphs.Count >= MaxGroundTelegraphs)
            {
                _telegraphs.RemoveAt(0);
            }

            _telegraphs.Add(new GroundTelegraphClientRow(packet.X, packet.Y, packet.RadiusTiles, now, packet.DelayMs));
        }
    }

    private void UpdateMonsterHp(int monsterId, int remainingHp)
    {
        lock (_gate)
        {
            for (int i = 0; i < _monsters.Count; i++)
            {
                if (_monsters[i].MonsterId == monsterId)
                {
                    if (remainingHp <= 0)
                    {
                        MonsterClientRow old = _monsters[i];
                        byte poofKind = (byte)(old.Kind >= 9 ? 2 : (old.Kind == 3 ? 2 : 0));
                        _deathEffects.Add(new DeathEffectClientRow(old.X, old.Y, Environment.TickCount64, 400f, poofKind));
                        _monsters.RemoveAt(i);
                    }
                    else
                    {
                        _monsters[i] = _monsters[i] with { Hp = remainingHp };
                    }

                    break;
                }
            }
        }

        this.MonstersUpdated?.Invoke();
    }
}
