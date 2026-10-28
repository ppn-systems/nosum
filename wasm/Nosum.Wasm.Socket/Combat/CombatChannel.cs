// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Combat;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Combat.S2C;
using Nosum.Gameplay.Skills;

namespace Nosum.Wasm.Socket.Combat;

/// <summary>Client-side snapshot of one monster on the active map.</summary>
/// <param name="MonsterId">Monster id within the map/floor.</param>
/// <param name="Kind">Monster species byte (<c>MonsterKind</c>).</param>
/// <param name="X">Tile X.</param>
/// <param name="Y">Tile Y.</param>
/// <param name="Hp">Current HP.</param>
/// <param name="MaxHp">Maximum HP.</param>
/// <param name="Modifier">Elite affix modifier (docs/adr/ADR-058).</param>
public readonly record struct MonsterClientRow(int MonsterId, byte Kind, int X, int Y, int Hp, int MaxHp, byte Modifier = 0);

/// <summary>One environmental hazard on the current map or dungeon floor (docs/adr/ADR-061).</summary>
/// <param name="HazardId">Hazard id.</param>
/// <param name="Kind">Hazard kind (1=SporeVent, 2=SpikeTrap, 3=MagmaFissure).</param>
/// <param name="X">Tile X.</param>
/// <param name="Y">Tile Y.</param>
/// <param name="Damage">Base damage per tick.</param>
public readonly record struct HazardClientRow(byte HazardId, byte Kind, ushort X, ushort Y, byte Damage);

/// <summary>Outcome of a client attack intent.</summary>
/// <param name="Status">Combat status.</param>
/// <param name="MonsterId">Target monster id.</param>
/// <param name="DamageDealt">Damage dealt to the monster.</param>
/// <param name="MonsterHp">Remaining monster HP.</param>
/// <param name="DamageTaken">Counter-attack damage taken by the player.</param>
/// <param name="KnockedOut">Whether the player was knocked out and revived.</param>
public readonly record struct AttackInfo(
    CombatStatus Status,
    int MonsterId,
    int DamageDealt,
    int MonsterHp,
    int DamageTaken,
    bool KnockedOut);

/// <summary>Summary of a cast skill result (docs/adr/ADR-056).</summary>
public readonly record struct SkillInfo(
    CombatStatus Status,
    byte SkillId,
    ushort DamageDealt,
    byte TargetsHit,
    ushort HealAmount,
    ushort CooldownMs);

/// <summary>Active in-flight spell or ranged projectile (docs/adr/ADR-048).</summary>
/// <param name="StartX">Origin tile X.</param>
/// <param name="StartY">Origin tile Y.</param>
/// <param name="TargetX">Destination tile X.</param>
/// <param name="TargetY">Destination tile Y.</param>
/// <param name="StartTime">Timestamp in milliseconds.</param>
/// <param name="DurationMs">Flight duration in milliseconds.</param>
/// <param name="Kind">Projectile kind (1 = Arcane, 2 = Fire, 3 = Spore).</param>
public readonly record struct ProjectileClientRow(
    float StartX,
    float StartY,
    float TargetX,
    float TargetY,
    long StartTime,
    float DurationMs,
    byte Kind);

/// <summary>
/// Client-side animated death poof / defeat particle effect (docs/adr/ADR-050).
/// </summary>
/// <param name="X">Tile X.</param>
/// <param name="Y">Tile Y.</param>
/// <param name="StartTime">Millisecond timestamp.</param>
/// <param name="DurationMs">Total lifetime of the poof.</param>
/// <param name="Kind">Poof visual row (0 = smoke, 1 = dust, 2 = explosion).</param>
public readonly record struct DeathEffectClientRow(
    int X,
    int Y,
    long StartTime,
    float DurationMs = 400f,
    byte Kind = 0);

/// <summary>
/// Client-side floating combat text (damage, crits, heals) rising above characters (docs/adr/ADR-055).
/// </summary>
/// <param name="WorldX">World pixel X.</param>
/// <param name="WorldY">World pixel Y.</param>
/// <param name="Amount">Combat numeric value.</param>
/// <param name="Style">0 = White, 1 = Gold/Crit, 2 = Red/DamageTaken, 3 = Green/Heal, 4 = Purple/Poison.</param>
/// <param name="StartTime">Millisecond timestamp.</param>
/// <param name="DurationMs">Total floating duration.</param>
public readonly record struct FloatingTextClientRow(
    int WorldX,
    int WorldY,
    int Amount,
    byte Style,
    long StartTime,
    float DurationMs = 650f);

/// <summary>
/// Client mirror of player HP &amp; MP vitals, Combat XP, and live monsters on the active map (docs/adr/ADR-045 D3).
/// </summary>
public sealed class CombatChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly IDisposable[] _subscriptions;
    private readonly object _gate = new();
    private readonly List<MonsterClientRow> _monsters = [];
    private readonly List<ProjectileClientRow> _projectiles = [];
    private readonly List<DeathEffectClientRow> _deathEffects = [];
    private readonly List<FloatingTextClientRow> _floatingTexts = [];
    private readonly List<HazardClientRow> _hazards = [];
    private readonly Dictionary<byte, long> _skillReadyAt = new();
    private int _playerX;
    private int _playerY;
    private byte _playerFacing;
    private int _activeSkillId;
    private long _activeSkillTick;

    private int _hp = 100;
    private int _maxHp = 100;
    private int _mp = 50;
    private int _maxMp = 50;
    private int _combatXp;
    private int _statusEffect;
    private int _comboCount;
    private long _comboExpireTick;
    private bool _hasInitialVitals;
    private int _lastCelebratedLevel;
    private int _levelUpValue;
    private long _levelUpTick;
    private string _mapId = string.Empty;

    /// <summary>Invoked when the player's active combo streak changes (docs/adr/ADR-060).</summary>
    public event Action? ComboChanged;

    /// <summary>Invoked when the player reaches a new Combat level (docs/adr/ADR-062).</summary>
    public event Action<int>? LevelledUp;

    /// <summary>Gets whether the celebratory level-up radiance banner is currently active.</summary>
    public bool IsLevelUpActive => Volatile.Read(ref _levelUpValue) > 0 && (Environment.TickCount64 - Volatile.Read(ref _levelUpTick)) < 3800;

    /// <summary>Gets the level currently being celebrated.</summary>
    public int ActiveLevelUpValue => Volatile.Read(ref _levelUpValue);

    /// <summary>Gets the current active combat combo count.</summary>
    public int CurrentCombo
    {
        get
        {
            if (Environment.TickCount64 > _comboExpireTick)
            {
                return 0;
            }

            return _comboCount;
        }
    }

    /// <summary>Initializes the combat channel.</summary>
    /// <param name="socket">The game connection.</param>
    public CombatChannel(IGameSocket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        _socket = socket;
        _subscriptions =
        [
            socket.Subscribe<VitalsState>(this.OnVitals),
            socket.Subscribe<MonstersSnapshot>(this.OnMonstersSnapshot),
            socket.Subscribe<MonsterChanged>(this.OnMonsterChanged),
        ];
    }

    /// <summary>Raised when HP, MP or Combat XP changes.</summary>
    public event Action? Changed;

    /// <summary>Raised when the monster list on the current map changes.</summary>
    public event Action? MonstersUpdated;

    /// <summary>Raised when an attack completes.</summary>
    public event Action<AttackInfo>? AttackCompleted;

    /// <summary>Raised when a potion drink request completes.</summary>
    public event Action<CombatStatus>? PotionCompleted;

    /// <summary>Raised when a skill cast request completes (docs/adr/ADR-056).</summary>
    public event Action<SkillInfo>? SkillCompleted;

    /// <summary>Gets current HP.</summary>
    public int Hp => Volatile.Read(ref _hp);

    /// <summary>Gets maximum HP.</summary>
    public int MaxHp => Volatile.Read(ref _maxHp);

    /// <summary>Gets current MP.</summary>
    public int Mp => Volatile.Read(ref _mp);

    /// <summary>Gets maximum MP.</summary>
    public int MaxMp => Volatile.Read(ref _maxMp);

    /// <summary>Gets cumulative Combat XP.</summary>
    public int CombatXp => Volatile.Read(ref _combatXp);

    /// <summary>Gets the active status effect kind (0 = None, 1 = Regen, 2 = Poison, 3 = Swiftness, docs/adr/ADR-051).</summary>
    public int StatusEffect => Volatile.Read(ref _statusEffect);

    /// <summary>Gets the map id of the current monster roster.</summary>
    public string MapId
    {
        get
        {
            lock (_gate)
            {
                return _mapId;
            }
        }
    }

    /// <summary>Copies the live monsters into <paramref name="destination"/> without allocating.</summary>
    /// <param name="destination">Destination list to populate.</param>
    public void CopyMonsters(List<MonsterClientRow> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        lock (_gate)
        {
            foreach (MonsterClientRow row in _monsters)
            {
                if (row.Hp > 0)
                {
                    destination.Add(row);
                }
            }
        }
    }

    /// <summary>Copies active environmental hazards into <paramref name="destination"/> without allocating.</summary>
    /// <param name="destination">Destination list to populate.</param>
    public void CopyHazards(List<HazardClientRow> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        lock (_gate)
        {
            destination.AddRange(_hazards);
        }
    }

    /// <summary>Finds the nearest live monster within <paramref name="maxRange"/> tiles of <c>(tileX, tileY)</c>.</summary>
    /// <param name="tileX">Player tile X.</param>
    /// <param name="tileY">Player tile Y.</param>
    /// <param name="maxRange">Maximum Chebyshev tile distance.</param>
    /// <param name="monster">The nearest monster when found.</param>
    /// <returns><see langword="true"/> when a live monster is in range.</returns>
    public bool TryFindTarget(int tileX, int tileY, int maxRange, out MonsterClientRow monster)
    {
        lock (_gate)
        {
            int bestDist = int.MaxValue;
            MonsterClientRow best = default;
            bool found = false;
            foreach (MonsterClientRow row in _monsters)
            {
                if (row.Hp <= 0)
                {
                    continue;
                }

                int dist = Math.Max(Math.Abs(row.X - tileX), Math.Abs(row.Y - tileY));
                if (dist <= maxRange && dist < bestDist)
                {
                    bestDist = dist;
                    best = row;
                    found = true;
                }
            }

            monster = best;
            return found;
        }
    }

    /// <summary>Finds a monster by its id.</summary>
    /// <param name="monsterId">Monster id to find.</param>
    /// <param name="monster">The monster if found.</param>
    /// <returns><see langword="true"/> when the monster is present.</returns>
    public bool TryGetMonster(int monsterId, out MonsterClientRow monster)
    {
        lock (_gate)
        {
            foreach (MonsterClientRow row in _monsters)
            {
                if (row.MonsterId == monsterId)
                {
                    monster = row;
                    return true;
                }
            }

            monster = default;
            return false;
        }
    }

    /// <summary>Requests current player vitals from the server.</summary>
    public void LoadVitals() => _ = this.LoadVitalsAsync();

    /// <summary>Requests current map monsters from the server.</summary>
    public void LoadMonsters() => _ = this.LoadMonstersAsync();

    /// <summary>Attacks <paramref name="monsterId"/> using the weapon/staff in <paramref name="slot"/>.</summary>
    /// <param name="monsterId">Target monster id.</param>
    /// <param name="slot">Hotbar/inventory slot.</param>
    public void Attack(int monsterId, int slot) => _ = this.AttackAsync(monsterId, slot);

    /// <summary>Drinks a potion in <paramref name="slot"/>.</summary>
    /// <param name="slot">Hotbar/inventory slot.</param>
    public void DrinkPotion(int slot) => _ = this.DrinkPotionAsync(slot);

    /// <summary>Gets the remaining cooldown in seconds for a skill (0 when ready) (docs/adr/ADR-056).</summary>
    /// <param name="skillId">The skill kind.</param>
    /// <returns>Seconds remaining until ready.</returns>
    public float GetSkillCooldownRemaining(byte skillId)
    {
        lock (_gate)
        {
            if (_skillReadyAt.TryGetValue(skillId, out long readyAt))
            {
                long diff = readyAt - Environment.TickCount64;
                return diff > 0 ? diff / 1000f : 0f;
            }

            return 0f;
        }
    }

    /// <summary>Casts a combat skill (1 = Whirlwind, 2 = Fireball, 3 = DivineHeal) (docs/adr/ADR-056).</summary>
    /// <param name="skillId">Skill kind.</param>
    /// <param name="targetMonsterId">Target monster id (0 for self/AoE, or auto-targets nearest within range 6).</param>
    public void CastSkill(byte skillId, int targetMonsterId = 0)
    {
        if (skillId == 2 && targetMonsterId == 0)
        {
            if (this.TryFindTarget(_playerX, _playerY, 6, out MonsterClientRow m))
            {
                targetMonsterId = m.MonsterId;
            }
        }

        this.TriggerSkillEffect(skillId, targetMonsterId);
        _ = this.CastSkillAsync(skillId, targetMonsterId);
    }

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

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable sub in _subscriptions)
        {
            sub.Dispose();
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Network errors are ignored; next action retries.")]
    private async Task LoadVitalsAsync()
    {
        try
        {
            using PacketScope<GetVitals> req = PacketFactory<GetVitals>.Acquire();
            VitalsState reply = await _socket.RequestAsync<VitalsState>(req.Value).ConfigureAwait(false);
            this.OnVitals(reply);
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Network errors are ignored; next map load retries.")]
    private async Task LoadMonstersAsync()
    {
        try
        {
            using PacketScope<GetMonsters> req = PacketFactory<GetMonsters>.Acquire();
            MonstersSnapshot reply = await _socket.RequestAsync<MonstersSnapshot>(req.Value).ConfigureAwait(false);
            this.OnMonstersSnapshot(reply);
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Network errors are ignored; player can swing again.")]
    private async Task AttackAsync(int monsterId, int slot)
    {
        try
        {
            using PacketScope<AttackMonster> req = PacketFactory<AttackMonster>.Acquire();
            req.Value.MonsterId = monsterId;
            req.Value.Slot = (byte)slot;
            AttackMonsterResult reply = await _socket.RequestAsync<AttackMonsterResult>(req.Value).ConfigureAwait(false);
            if (reply.MaxHp > 0)
            {
                Volatile.Write(ref _hp, reply.Hp);
                Volatile.Write(ref _maxHp, reply.MaxHp);
                Volatile.Write(ref _mp, reply.Mp);
                Volatile.Write(ref _maxMp, reply.MaxMp);
                Volatile.Write(ref _combatXp, reply.CombatXp);
                this.CheckCombatLevelUp(reply.CombatXp);
                this.Changed?.Invoke();
            }

            if (reply.Status == CombatStatus.Ok)
            {
                if (reply.ComboCount > 0)
                {
                    _comboCount = reply.ComboCount;
                    _comboExpireTick = Environment.TickCount64 + 3500;
                    this.ComboChanged?.Invoke();
                }

                if (this.TryGetMonster(reply.MonsterId, out MonsterClientRow m))
                {
                    byte style = (byte)(reply.DamageDealt >= 18 || reply.ComboCount >= 3 ? 1 : 0);
                    this.SpawnFloatingText((m.X * 16) + 8, (m.Y * 16) - 6, reply.DamageDealt, style);
                }

                if (reply.DamageTaken > 0)
                {
                    this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 6, reply.DamageTaken, 2);
                }

                this.UpdateMonsterHp(reply.MonsterId, reply.MonsterHp);
            }

            this.AttackCompleted?.Invoke(new AttackInfo(
                reply.Status,
                reply.MonsterId,
                reply.DamageDealt,
                reply.MonsterHp,
                reply.DamageTaken,
                reply.KnockedOut));
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Network errors are ignored.")]
    private async Task DrinkPotionAsync(int slot)
    {
        try
        {
            using PacketScope<DrinkPotion> req = PacketFactory<DrinkPotion>.Acquire();
            req.Value.Slot = (byte)slot;
            DrinkPotionResult reply = await _socket.RequestAsync<DrinkPotionResult>(req.Value).ConfigureAwait(false);
            if (reply.MaxHp > 0)
            {
                int oldHp = Volatile.Read(ref _hp);
                int hpDiff = reply.Hp - oldHp;
                if (hpDiff > 0)
                {
                    this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 6, hpDiff, 3);
                }

                Volatile.Write(ref _hp, reply.Hp);
                Volatile.Write(ref _maxHp, reply.MaxHp);
                Volatile.Write(ref _mp, reply.Mp);
                Volatile.Write(ref _maxMp, reply.MaxMp);
                this.Changed?.Invoke();
            }

            this.PotionCompleted?.Invoke(reply.Status);
        }
        catch (Exception)
        {
        }
    }

    [SuppressMessage("Design", "CA1031", Justification = "Network errors are ignored.")]
    private async Task CastSkillAsync(byte skillId, int targetMonsterId)
    {
        try
        {
            using PacketScope<CastSkill> req = PacketFactory<CastSkill>.Acquire();
            req.Value.SkillId = skillId;
            req.Value.TargetMonsterId = targetMonsterId;
            CastSkillResult reply = await _socket.RequestAsync<CastSkillResult>(req.Value).ConfigureAwait(false);

            if (reply.Status == CombatStatus.Ok)
            {
                lock (_gate)
                {
                    _skillReadyAt[reply.SkillId] = Environment.TickCount64 + reply.CooldownMs;
                }

                if (reply.SkillId == 3 && reply.HealAmount > 0)
                {
                    this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 6, reply.HealAmount, 3);
                }
                else if (reply.SkillId == 2 && reply.DamageDealt > 0)
                {
                    if (this.TryGetMonster(targetMonsterId, out MonsterClientRow tm))
                    {
                        this.SpawnProjectile(_playerX, _playerY, tm.X, tm.Y, 1);
                        this.SpawnFloatingText((tm.X * 16) + 8, (tm.Y * 16) - 6, reply.DamageDealt, 1);
                    }
                }
                else if (reply.SkillId == 1 && reply.DamageDealt > 0)
                {
                    this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 10, reply.DamageDealt, 1);
                }

                if (reply.ComboCount > 0)
                {
                    _comboCount = reply.ComboCount;
                    _comboExpireTick = Environment.TickCount64 + 3500;
                    this.ComboChanged?.Invoke();
                }

                if (reply.MaxHp > 0)
                {
                    Volatile.Write(ref _hp, reply.Hp);
                    Volatile.Write(ref _maxHp, reply.MaxHp);
                    Volatile.Write(ref _mp, reply.Mp);
                    Volatile.Write(ref _maxMp, reply.MaxMp);
                    Volatile.Write(ref _combatXp, reply.CombatXp);
                    this.CheckCombatLevelUp(reply.CombatXp);
                    this.Changed?.Invoke();
                }
            }

            this.SkillCompleted?.Invoke(new SkillInfo(
                reply.Status,
                reply.SkillId,
                reply.DamageDealt,
                reply.TargetsHit,
                reply.HealAmount,
                reply.CooldownMs));
        }
        catch (Exception)
        {
        }
    }

    private void OnVitals(VitalsState packet)
    {
        if (packet.MaxHp <= 0)
        {
            return;
        }

        int oldHp = Volatile.Read(ref _hp);
        int oldMp = Volatile.Read(ref _mp);
        int diff = packet.Hp - oldHp;
        int mpDiff = packet.Mp - oldMp;
        if (oldHp > 0 && diff != 0)
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
        else if (oldMp > 0 && mpDiff > 0)
        {
            this.SpawnFloatingText((_playerX * 16) + 8, (_playerY * 16) - 6, mpDiff, 3);
        }

        Volatile.Write(ref _hp, packet.Hp);
        Volatile.Write(ref _maxHp, packet.MaxHp);
        Volatile.Write(ref _mp, packet.Mp);
        Volatile.Write(ref _maxMp, packet.MaxMp);
        Volatile.Write(ref _combatXp, packet.CombatXp);
        Volatile.Write(ref _statusEffect, (int)packet.StatusEffect);
        this.CheckCombatLevelUp(packet.CombatXp);
        this.Changed?.Invoke();
    }

    /// <summary>Triggers the celebratory level-up radiance beacon and banner (docs/adr/ADR-062).</summary>
    /// <param name="level">New level reached.</param>
    public void TriggerLevelUp(int level)
    {
        if (level <= 0 || level <= _lastCelebratedLevel)
        {
            return;
        }

        _lastCelebratedLevel = level;
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
            this.TriggerLevelUp(newLevel);
        }
    }

    private async Task ExpireLevelUpBannerAsync()
    {
        await Task.Delay(3900).ConfigureAwait(false);
        this.Changed?.Invoke();
    }

    private void OnMonstersSnapshot(MonstersSnapshot packet)
    {
        lock (_gate)
        {
            _mapId = packet.MapId ?? string.Empty;
            _monsters.Clear();
            foreach (MonsterRow row in packet.Monsters)
            {
                if (row.Hp > 0)
                {
                    _monsters.Add(new MonsterClientRow(row.MonsterId, row.Kind, row.X, row.Y, row.Hp, row.MaxHp, row.Modifier));
                }
            }

            _hazards.Clear();
            if (packet.Hazards is not null)
            {
                foreach (HazardRow h in packet.Hazards)
                {
                    _hazards.Add(new HazardClientRow(h.HazardId, h.Kind, h.X, h.Y, h.Damage));
                }
            }
        }

        this.MonstersUpdated?.Invoke();
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
                        _monsters[i] = new MonsterClientRow(packet.MonsterId, packet.Kind, packet.X, packet.Y, packet.Hp, packet.MaxHp, packet.Modifier);
                    }

                    found = true;
                    break;
                }
            }

            if (!found && packet.Hp > 0)
            {
                _monsters.Add(new MonsterClientRow(packet.MonsterId, packet.Kind, packet.X, packet.Y, packet.Hp, packet.MaxHp, packet.Modifier));
            }
        }

        this.MonstersUpdated?.Invoke();
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
